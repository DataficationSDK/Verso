using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Verso.Abstractions;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.PackageManagement;

namespace Verso.Python.Host;

/// <summary>
/// Drives one Python subprocess for the lifetime of a kernel: resolves which interpreter to run,
/// starts it, turns cell execution into protocol traffic, and translates the events that come back
/// into calls on the execution context. Interrupt escalation and crash recovery live here too,
/// because both need the process handle and the in-flight request at the same time.
/// </summary>
internal sealed class PythonHostSession : IAsyncDisposable
{
    private readonly PythonKernelOptions _options;
    private readonly InterpreterLocator _locator;
    private readonly ManagedEnvironment _managedEnvironment;
    private readonly HostBootstrap _bootstrap;

    private readonly Dictionary<string, string> _injectedHashes = new(StringComparer.Ordinal);

    /// <summary>Why each name currently cannot be shared, so the reason is given once per change.</summary>
    private readonly Dictionary<string, string> _unavailable = new(StringComparer.Ordinal);

    /// <summary>Names whose shareability changed while building the current cell's payload.</summary>
    private readonly List<(string Name, string? Reason)> _statusChanges = new();

    /// <summary>Names already reported as too large to publish, so each is mentioned once.</summary>
    private readonly HashSet<string> _oversizeReported = new(StringComparer.Ordinal);

    private readonly AutoInstallService _autoInstall;

    private PythonHostProcess? _process;
    private Dictionary<string, object>? _preExecutionSnapshot;
    private string _executable = "";
    private string _workingDirectory = "";
    private string? _sessionSelection;
    private volatile bool _executing;
    private bool _boundToNotebook;
    private bool _dependenciesRequested;
    private bool _requiresPythonReported;
    private bool _pythonDllReported;
    private int _crashExitCode;

    /// <summary>Assembles streamed text into terminal-like lines. Replaced for each cell.</summary>
    private StreamLineAssembler _lines = new();

    /// <summary>Where each revisable block sits in the outputs this cell will return.</summary>
    private readonly Dictionary<string, int> _blockIndexes = new(StringComparer.Ordinal);
    private bool _crashed;
    private bool _disposed;

    public PythonHostSession(
        PythonKernelOptions options,
        string? sessionSelection = null,
        InterpreterLocator? locator = null,
        ManagedEnvironment? managedEnvironment = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sessionSelection = sessionSelection;
        _locator = locator ?? new InterpreterLocator(new InterpreterValidator());
        _managedEnvironment = managedEnvironment ?? new ManagedEnvironment();
        _bootstrap = new HostBootstrap(
            options.DefaultImports,
            options.StartupCode,
            options.EnableShellEscapes,
            options.VariablePublishLimitBytes);
        _autoInstall = new AutoInstallService(options);
    }

    /// <summary>
    /// The install policy engine, exposed so the kernel can hand it a settings change and so the
    /// tests can redirect installs at a local wheel directory.
    /// </summary>
    internal AutoInstallService AutoInstall => _autoInstall;

    /// <summary>The interpreter the subprocess is running, once one has been resolved.</summary>
    public InterpreterInfo? Interpreter { get; private set; }

    public bool IsAlive => _process is { IsAlive: true };

    /// <summary>
    /// Records an interpreter chosen with <c>#!python</c>. Held here rather than only in the
    /// variable store because a restart clears the store before the kernel starts again.
    /// </summary>
    public void SetSessionInterpreter(string? executable)
    {
        if (string.Equals(_sessionSelection, executable, StringComparison.Ordinal))
            return;

        _sessionSelection = executable;

        // The next start must reconsider which interpreter to run.
        _boundToNotebook = false;
    }

    /// <summary>
    /// Resolve an interpreter and spawn the subprocess. Called from kernel initialization, which
    /// has no notebook context, so discovery runs against the process working directory; the first
    /// execution rebinds to the notebook's own directory when they differ.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var directory = Environment.CurrentDirectory;
        var executable = await ResolveExecutableAsync(directory, cancellationToken).ConfigureAwait(false);
        await StartProcessAsync(executable, directory, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        ThrowIfDisposed();

        var outputs = new List<CellOutput>();

        // Both are per cell: a block written by the previous cell cannot be revised by this one.
        _lines = new StreamLineAssembler();
        _blockIndexes.Clear();

        await ReportIgnoredPythonDllAsync(context, outputs).ConfigureAwait(false);

        PythonHostProcess process;
        PythonHostConnection connection;
        try
        {
            await PrepareForExecutionAsync(context, outputs).ConfigureAwait(false);

            process = _process
                ?? throw new PythonHostException("The Python host is not running.");
            connection = process.Connection
                ?? throw new PythonHostException("The Python host is not connected.");
        }
        catch (PythonHostException ex)
        {
            // No interpreter, or one that will not start. This is the actionable message the
            // locator built, so it is reported as the cell's error rather than thrown.
            await EmitAsync(CellOutput.Error(ex.Message, "PythonHostUnavailable"), context, outputs)
                .ConfigureAwait(false);
            return outputs;
        }

        // Anything the cell imports that the environment cannot provide is dealt with before the
        // cell runs, so the install lands in time for the import rather than after it fails.
        await EnsurePackagesAsync(code, context, outputs).ConfigureAwait(false);

        // Taken before the request goes out, so a value another kernel writes while this cell
        // runs can be told apart from one the cell itself produced.
        SnapshotStore(context.Variables);

        var request = new JsonObject
        {
            [HostProtocol.TypeField] = HostProtocol.Execute,
            [HostProtocol.CodeField] = code,
            [HostProtocol.InjectField] = BuildInjectPayload(context.Variables),
            [HostProtocol.PublishField] = JsonValue.Create(_options.PublishVariables),
        };

        var events = Channel.CreateUnbounded<JsonObject>(
            new UnboundedChannelOptions { SingleReader = true });

        // Counted as frames arrive rather than as they are consumed, because the backstop below
        // has to know whether the cell wrote anything before it failed, and the consumer runs
        // concurrently. Frames are dispatched in order on the read pump, so every event that
        // preceded the reply has already been counted by the time the reply completes.
        var emitted = 0;

        void OnEvent(JsonObject message)
        {
            if (HostProtocol.GetMessageType(message) is HostProtocol.Stream or HostProtocol.Display)
                Interlocked.Increment(ref emitted);

            events.Writer.TryWrite(message);
        }

        connection.EventReceived += OnEvent;

        // Editor requests are answered as empty while this is set: they read the same namespace
        // the cell is changing, and an answer that waited out a long cell would arrive against a
        // cursor the user has left.
        _executing = true;

        try
        {
            // Not cancelled through the token: interrupt escalation below decides when to give up
            // on a reply, and a cancelled registration would lose the result the host still sends.
            var reply = connection.SendRequestAsync(request, CancellationToken.None);
            var consumer = ConsumeEventsAsync(events.Reader, context, outputs);

            var completedNormally = await AwaitReplyAsync(reply, process, context, outputs).ConfigureAwait(false);

            // The backstop runs while the event channel is still open: a retried cell produces
            // its own stream and display events, and completing the writer first would drop them.
            JsonObject? finalReply = null;
            if (completedNormally)
            {
                finalReply = await ApplyImportBackstopAsync(
                    request,
                    await reply.ConfigureAwait(false),
                    () => Volatile.Read(ref emitted) == 0,
                    connection,
                    process,
                    context,
                    outputs).ConfigureAwait(false);
            }

            events.Writer.TryComplete();
            await consumer.ConfigureAwait(false);

            if (finalReply is not null)
            {
                await AppendReplyAsync(finalReply, context, outputs).ConfigureAwait(false);
            }
            else
            {
                // The process was stopped mid-cell, so this request will never be answered.
                // Observing the failure keeps it from surfacing later as an unobserved exception.
                _ = reply.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
            }
        }
        catch (PythonHostException ex)
        {
            events.Writer.TryComplete();
            await ReportCrashAsync(ex, context, outputs).ConfigureAwait(false);
        }
        finally
        {
            _executing = false;
            connection.EventReceived -= OnEvent;
        }

        return outputs;
    }

    // --- package management ---

    /// <summary>
    /// How long an import scan waits for its answer. The scan is an optimization over failing at
    /// the import, so an answer that does not arrive promptly is dropped and the cell runs.
    /// </summary>
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ask the interpreter what it cannot satisfy for this cell, then install what the policy
    /// allows. Everything here is best effort: a cell always runs, whether or not the scan
    /// answered and whether or not the install worked.
    /// </summary>
    private async Task EnsurePackagesAsync(string code, IExecutionContext context, List<CellOutput> outputs)
    {
        if (Interpreter is not { } interpreter)
            return;

        var requirements = new List<string>();

        if (Pep723Block.TryRead(code, out var metadata))
        {
            requirements.AddRange(metadata.Requirements);

            // Reported whatever the install policy is: the interpreter in use not meeting a
            // declared floor is worth knowing even when nothing here may install anything.
            await ReportRequiresPythonAsync(metadata.RequiresPython, interpreter, context, outputs)
                .ConfigureAwait(false);
        }

        if (!_autoInstall.Enabled)
            return;

        // The notebook's declared list is checked once for the session, per its contract. It is
        // sent on the first cell that gets this far, whichever cell the user ran.
        if (!_dependenciesRequested && _autoInstall.Dependencies.Count > 0)
        {
            requirements.AddRange(_autoInstall.Dependencies);
            _dependenciesRequested = true;
        }

        if (!_autoInstall.ShouldScan(code, requirements))
            return;

        var scan = await ScanAsync(code, requirements).ConfigureAwait(false);
        if (scan is null)
        {
            // No answer, so nothing is known to be missing. A cell that does need something
            // still reaches the backstop when the import fails.
            _dependenciesRequested = false;
            return;
        }

        await _autoInstall
            .EnsureAsync(scan, interpreter.Executable, Describe(interpreter), context)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The distributions this cell's imports need, or null when the subprocess did not answer.
    /// The two cases are distinguished so a scan that never ran can be retried.
    /// </summary>
    private async Task<ImportScan?> ScanAsync(string code, IReadOnlyList<string> requirements)
    {
        if (_disposed)
            return null;

        var process = _process is { IsAlive: true } alive ? alive : null;
        var connection = process?.Connection;
        if (process is null || connection is null)
            return null;

        // A host script from an earlier release ignores the request rather than answering it,
        // which would cost the cell the whole deadline for nothing.
        if (process.Handshake?.Capabilities.Contains(HostProtocol.ScanCapability) != true)
            return null;

        var request = new JsonObject
        {
            [HostProtocol.TypeField] = HostProtocol.ScanImports,
            [HostProtocol.CodeField] = code,
        };

        if (requirements.Count > 0)
        {
            var declared = new JsonArray();
            foreach (var requirement in requirements)
                declared.Add(JsonValue.Create(requirement));
            request[HostProtocol.RequirementsField] = declared;
        }

        try
        {
            using var deadline = new CancellationTokenSource(ScanTimeout);
            var reply = await connection.SendRequestAsync(request, deadline.Token).ConfigureAwait(false);
            return HostPackages.MapScan(reply);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (PythonHostException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// A cell can reach an import the scan could not see, either because it is built at runtime
    /// or because it sits behind a call the scan does not follow. The failure names the module,
    /// which is enough to resolve and install it.
    /// <para>
    /// The cell is only run again when it had produced no output before failing. Output already
    /// written to a cell cannot be retracted, so a retry after a partial run would show the
    /// first attempt's lines twice.
    /// </para>
    /// </summary>
    /// <returns>The reply to report: the retry's when there was one, otherwise the original.</returns>
    private async Task<JsonObject> ApplyImportBackstopAsync(
        JsonObject request,
        JsonObject reply,
        Func<bool> cellIsSilent,
        PythonHostConnection connection,
        PythonHostProcess process,
        IExecutionContext context,
        List<CellOutput> outputs)
    {
        if (!_autoInstall.Enabled || Interpreter is not { } interpreter)
            return reply;

        if (HostProtocol.TryGetString(reply, HostProtocol.StatusField) != HostProtocol.StatusError)
            return reply;

        if (reply[HostProtocol.ErrorField] is not JsonObject error)
            return reply;

        var module = HostProtocol.TryGetString(error, HostProtocol.MissingModuleField);
        if (string.IsNullOrWhiteSpace(module))
            return reply;

        var silent = cellIsSilent();

        var installed = await _autoInstall
            .EnsureModuleAsync(module!, interpreter.Executable, Describe(interpreter), context)
            .ConfigureAwait(false);

        if (installed is null)
            return reply;

        if (!silent)
        {
            await EmitAsync(
                CellOutput.Plain($"{installed} is installed now. Run the cell again to continue."),
                context, outputs).ConfigureAwait(false);
            return reply;
        }

        if (!process.IsAlive || _crashed)
            return reply;

        try
        {
            await EmitAsync(CellOutput.Plain("Running the cell again."), context, outputs)
                .ConfigureAwait(false);

            return await connection.SendRequestAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (PythonHostException)
        {
            return reply;
        }
        catch (ObjectDisposedException)
        {
            return reply;
        }
        catch (InvalidOperationException)
        {
            return reply;
        }
    }

    private async Task ReportRequiresPythonAsync(
        string? specifier,
        InterpreterInfo interpreter,
        IExecutionContext context,
        List<CellOutput> outputs)
    {
        if (_requiresPythonReported || string.IsNullOrWhiteSpace(specifier))
            return;

        if (RequiresPython.IsSatisfied(specifier, interpreter.Version))
            return;

        // Said once: the interpreter cannot be swapped underneath a running session, so
        // repeating it on every cell would only add noise.
        _requiresPythonReported = true;

        await EmitAsync(
            CellOutput.Plain(
                $"This notebook declares Python {specifier} and is running {interpreter.RawVersion}. " +
                "Use #!python to select a different interpreter, then restart the kernel."),
            context, outputs).ConfigureAwait(false);
    }

    /// <summary>
    /// Tell an embedder that a shared-library path it set is doing nothing. Only reachable from
    /// code that configured the option directly, and said once for the same reason the version
    /// note is: the answer cannot change while the session runs.
    /// </summary>
    private async Task ReportIgnoredPythonDllAsync(IExecutionContext context, List<CellOutput> outputs)
    {
#pragma warning disable CS0618 // Reporting the obsolete option is the point.
        if (_pythonDllReported || string.IsNullOrWhiteSpace(_options.PythonDll))
            return;
#pragma warning restore CS0618

        _pythonDllReported = true;

        await EmitAsync(
            CellOutput.Plain(
                "PythonKernelOptions.PythonDll is ignored. Python runs as a separate process and " +
                "loads no shared library. Name an interpreter with PythonExecutable, the " +
                "VERSO_PYTHON environment variable, or #!python."),
            context, outputs).ConfigureAwait(false);
    }

    private static string Describe(InterpreterInfo interpreter)
        => $"Python {interpreter.RawVersion} ({interpreter.EnvironmentKind}) at {interpreter.Executable}";

    // --- editor assistance ---

    /// <summary>
    /// How long an editor request waits for its answer. Nothing here is worth interrupting the
    /// interpreter over, so a request that outlives this is abandoned rather than chased.
    /// <para>
    /// The budget is set by the first request rather than the typical one. Answers arrive in
    /// milliseconds once the analysis has read the standard library's type information, but the
    /// reading itself takes seconds on a modest machine, and cutting that off would leave the
    /// first question of a session unanswered on exactly the machines that can least afford to
    /// ask it twice.
    /// </para>
    /// </summary>
    private static readonly TimeSpan IntelliSenseTimeout = TimeSpan.FromSeconds(10);

    public async Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        var request = new JsonObject
        {
            [HostProtocol.TypeField] = HostProtocol.Complete,
            [HostProtocol.CodeField] = code,
            [HostProtocol.CursorField] = cursorPosition,
        };

        return HostIntelliSense.MapCompletions(await RequestAssistanceAsync(request).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        var request = new JsonObject
        {
            [HostProtocol.TypeField] = HostProtocol.Diagnostics,
            [HostProtocol.CodeField] = code,
        };

        return HostIntelliSense.MapDiagnostics(await RequestAssistanceAsync(request).ConfigureAwait(false));
    }

    public async Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        var request = new JsonObject
        {
            [HostProtocol.TypeField] = HostProtocol.Hover,
            [HostProtocol.CodeField] = code,
            [HostProtocol.CursorField] = cursorPosition,
        };

        var reply = await RequestAssistanceAsync(request).ConfigureAwait(false);
        return HostIntelliSense.MapHover(reply, code, cursorPosition);
    }

    /// <summary>
    /// Send an editor request and return its reply, or null when there is nothing to ask, nobody
    /// to ask, or no answer in time. Every one of those means the same thing to the caller.
    /// </summary>
    private async Task<JsonObject?> RequestAssistanceAsync(JsonObject request)
    {
        if (_disposed || _executing)
            return null;

        var connection = _process is { IsAlive: true } process ? process.Connection : null;
        if (connection is null)
            return null;

        try
        {
            using var deadline = new CancellationTokenSource(IntelliSenseTimeout);
            return await connection.SendRequestAsync(request, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The deadline passed. The registration is abandoned, so a late reply is discarded.
            return null;
        }
        catch (PythonHostException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            // The subprocess went away between the check above and the send.
            return null;
        }
        catch (InvalidOperationException)
        {
            // The connection has not finished its handshake.
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_process is not null)
        {
            await _process.DisposeAsync().ConfigureAwait(false);
            _process = null;
        }
    }

    // --- startup and rebinding ---

    private async Task<string> ResolveExecutableAsync(string? notebookDirectory, CancellationToken cancellationToken)
    {
        var query = new InterpreterQuery(
            ExplicitExecutable: _options.PythonExecutable,
            SessionSelection: _sessionSelection,
            NotebookDirectory: notebookDirectory,
            WorkspaceRoot: notebookDirectory);

        var resolution = await _locator.ResolveAsync(query, cancellationToken).ConfigureAwait(false);
        if (!resolution.Found)
            throw new PythonHostException(resolution.BuildNotFoundMessage());

        var interpreter = resolution.Interpreter!;

        // A base interpreter carrying a PEP 668 marker cannot be installed into, so package
        // management gets a writable layer that still sees everything the base provides.
        if (!interpreter.IsExternallyManaged)
        {
            Interpreter = interpreter;
            return interpreter.Executable;
        }

        var derived = await _managedEnvironment
            .EnsureDerivedEnvironmentAsync(interpreter, _options.UseUv, cancellationToken)
            .ConfigureAwait(false);

        Interpreter = interpreter with { Executable = derived, Source = InterpreterSource.DerivedEnvironment };
        return derived;
    }

    private async Task StartProcessAsync(string executable, string workingDirectory, CancellationToken cancellationToken)
    {
        if (_process is not null)
        {
            await _process.DisposeAsync().ConfigureAwait(false);
            _process = null;
        }

        var process = new PythonHostProcess(executable, workingDirectory, BuildBootstrap());
        process.Exited += OnProcessExited;

        // The new interpreter has an empty scope, so nothing counts as already injected, and
        // anything explained about a name earlier is worth explaining again to the new scope.
        _injectedHashes.Clear();
        _unavailable.Clear();
        _statusChanges.Clear();
        _oversizeReported.Clear();

        await process.StartAsync(cancellationToken).ConfigureAwait(false);

        _process = process;
        _executable = executable;
        _workingDirectory = workingDirectory;
        _crashed = false;

        ProvisionToolsInBackground(process);
    }

    /// <summary>
    /// The bootstrap for this start. Everything but the analysis tools directory is fixed for the
    /// session; that directory is keyed by the interpreter's version, so it is composed here,
    /// once the interpreter is known.
    /// </summary>
    private HostBootstrap BuildBootstrap()
        => Interpreter is { } interpreter
            ? _bootstrap with { JediToolsPath = JediTools.ComputeToolsPath(interpreter.Version) }
            : _bootstrap;

    /// <summary>
    /// Install the analysis tools when the interpreter does not already carry them. Deliberately
    /// not awaited: it can involve a download, and the startup budget belongs to the first cell.
    /// The subprocess has the directory on its import path from bootstrap and picks the tools up
    /// once they land.
    /// </summary>
    private void ProvisionToolsInBackground(PythonHostProcess process)
    {
        if (Interpreter is not { } interpreter)
            return;

        // The handshake reports whether the interpreter's own environment already provides them,
        // in which case a second copy would be a download for nothing.
        if (process.Handshake?.Capabilities.Contains(HostProtocol.JediCapability) == true)
            return;

        _ = Task.Run(() => JediTools.EnsureProvisionedAsync(
            interpreter.Executable, interpreter.Version, _options.UseUv, CancellationToken.None));
    }

    private void OnProcessExited(int exitCode)
    {
        _crashExitCode = exitCode;
        _crashed = true;
    }

    /// <summary>
    /// Bring the subprocess into the state this execution needs: bound to the notebook's directory
    /// and interpreter on the first cell, and alive after a crash.
    /// </summary>
    /// <summary>
    /// Bind the session to a notebook's directory and interpreter, restarting the subprocess when
    /// either differs from what it was started with. Initialization runs without a notebook, so
    /// this is the first point at which either is known. Rebinding costs nothing here: no cell has
    /// run, so there is no interpreter state to lose.
    /// </summary>
    /// <returns>Whether the subprocess was started or restarted by this call.</returns>
    public async Task<bool> EnsureBoundAsync(string? notebookDirectory, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (_boundToNotebook)
            return false;

        var directory = notebookDirectory ?? Environment.CurrentDirectory;
        var executable = await ResolveExecutableAsync(directory, cancellationToken).ConfigureAwait(false);

        // Only marked once resolution succeeded, so a failure here is retried on the next run
        // rather than leaving the session bound to an interpreter it never found.
        _boundToNotebook = true;

        if (_process is null || !PathsEqual(directory, _workingDirectory) || !PathsEqual(executable, _executable))
        {
            await StartProcessAsync(executable, directory, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async Task PrepareForExecutionAsync(IExecutionContext context, List<CellOutput> outputs)
    {
        var cancellationToken = context.CancellationToken;

        if (await EnsureBoundAsync(NotebookDirectory(context), cancellationToken).ConfigureAwait(false))
            return;

        if (_process is not null && _process.IsAlive && !_crashed)
            return;

        var note = _crashed
            ? $"The Python process exited (exit code {_crashExitCode}) and has been restarted. " +
              "Variables and imports from earlier cells are no longer available."
            : "The Python process has been restarted. " +
              "Variables and imports from earlier cells are no longer available.";

        await StartProcessAsync(_executable, _workingDirectory, cancellationToken).ConfigureAwait(false);
        await EmitAsync(CellOutput.Plain(note), context, outputs).ConfigureAwait(false);
    }

    // --- request lifetime ---

    /// <summary>
    /// Wait for the execute reply, escalating through interrupt and then a kill when the cell is
    /// cancelled. Returns whether the reply arrived and should be reported.
    /// </summary>
    private async Task<bool> AwaitReplyAsync(
        Task<JsonObject> reply, PythonHostProcess process, IExecutionContext context, List<CellOutput> outputs)
    {
        var cancellationToken = context.CancellationToken;
        if (!cancellationToken.CanBeCanceled)
        {
            await reply.ConfigureAwait(false);
            return true;
        }

        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), cancelled))
        {
            if (await Task.WhenAny(reply, cancelled.Task).ConfigureAwait(false) == reply)
                return true;
        }

        // Signal first. Well-behaved Python code raises KeyboardInterrupt and the host still
        // answers with a cancelled result, which keeps the interpreter and its state alive.
        await process.TryInterruptAsync(CancellationToken.None).ConfigureAwait(false);

        var grace = Task.Delay(_options.InterruptGracePeriod);
        if (await Task.WhenAny(reply, grace).ConfigureAwait(false) == reply)
            return true;

        // Native code can ignore signals outright, so the only remaining lever is termination.
        process.Kill();
        await EmitAsync(
            CellOutput.Plain(
                "Execution was cancelled. The Python process did not respond to the interrupt " +
                "within the grace period and was stopped, so interpreter state was reset."),
            context, outputs).ConfigureAwait(false);

        try { await process.RespawnAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { /* the next execution retries the restart and reports its own failure */ }

        _crashed = false;
        return false;
    }

    private async Task ConsumeEventsAsync(
        ChannelReader<JsonObject> reader, IExecutionContext context, List<CellOutput> outputs)
    {
        // Events are handled one at a time and in arrival order, so interleaved stdout, stderr, and
        // display output reaches the notebook in the order the cell produced it.
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var message))
            {
                try { await HandleEventAsync(message, context, outputs).ConfigureAwait(false); }
                catch { /* one malformed event must not abandon the rest of the cell's output */ }
            }
        }
    }

    private async Task HandleEventAsync(JsonObject message, IExecutionContext context, List<CellOutput> outputs)
    {
        switch (HostProtocol.GetMessageType(message))
        {
            case HostProtocol.Stream:
            {
                var text = HostProtocol.TryGetString(message, HostProtocol.TextField);
                if (string.IsNullOrEmpty(text)) return;

                // A stream event carries an error name only when the host has something to report
                // that no reply can carry, such as an exception on a thread the cell started.
                // Ordinary stream text is recorded by channel and is not a failure: plenty of
                // programs write progress and logging to standard error while succeeding.
                var errorName = HostProtocol.TryGetString(message, HostProtocol.ErrorField);
                var channel = HostProtocol.TryGetString(message, HostProtocol.NameField) == HostProtocol.StreamStderr
                    ? OutputChannel.Stderr
                    : OutputChannel.Stdout;

                if (!string.IsNullOrEmpty(errorName))
                {
                    // A reported failure is its own output rather than part of the running text,
                    // so it is never revised and never overwritten by a later redraw.
                    await EmitAsync(
                        new CellOutput("text/plain", text!, IsError: true, ErrorName: errorName) { Channel = channel },
                        context, outputs).ConfigureAwait(false);
                    return;
                }

                var assembled = _lines.Append(channel, text!);
                if (assembled is null)
                    return;

                await ReviseAsync(
                    _lines.BlockId,
                    new CellOutput("text/plain", assembled) { Channel = channel },
                    context, outputs).ConfigureAwait(false);
                return;
            }

            case HostProtocol.Display:
            {
                await EmitAsync(MapPayload(message), context, outputs).ConfigureAwait(false);
                return;
            }

            case HostProtocol.InputRequest:
            {
                await AnswerInputAsync(message, context).ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task AnswerInputAsync(JsonObject message, IExecutionContext context)
    {
        var requestId = HostProtocol.TryGetInt(message, HostProtocol.ReqIdField) ?? HostProtocol.UnsolicitedRequestId;
        var prompt = HostProtocol.TryGetString(message, HostProtocol.PromptField) ?? "";
        var isPassword = message[HostProtocol.PasswordField] is JsonValue flag
            && flag.TryGetValue<bool>(out var password) && password;

        string? value = null;
        try
        {
            value = await context.RequestInputAsync(prompt, isPassword, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // A host without interactive input, or a cancelled prompt: Python sees end of input,
            // which is what a non-interactive interpreter reports.
        }

        var connection = _process?.Connection;
        if (connection is null) return;

        var reply = new JsonObject
        {
            [HostProtocol.TypeField] = HostProtocol.InputReply,
            [HostProtocol.ReqIdField] = requestId,
            [HostProtocol.ValueField] = value is null ? null : JsonValue.Create(value),
        };

        try { await connection.SendAsync(reply, CancellationToken.None).ConfigureAwait(false); }
        catch { /* the process is gone; the pending execute reports it */ }
    }

    /// <summary>
    /// Turn a MIME payload into the output shape the notebook renderers expect. Raster images are
    /// wrapped as HTML elements rather than passed through as image outputs, because the narrower
    /// renderer used for collapsed and non-editable cells shows only text, HTML, and SVG, and a
    /// bare image there would appear as its Base64 text.
    /// </summary>
    internal static CellOutput MapPayload(string mime, string data) => mime switch
    {
        "text/html" => CellOutput.Html(data),
        "image/svg+xml" => CellOutput.Svg(data),
        "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "image/bmp" =>
            CellOutput.Html($"<img src=\"data:{mime};base64,{data}\" />"),
        "application/json" => CellOutput.Json(data),
        "text/plain" => CellOutput.Plain(data),
        _ => new CellOutput(mime, data),
    };

    private static CellOutput MapPayload(JsonObject payload)
        => MapPayload(
            HostProtocol.TryGetString(payload, HostProtocol.MimeField) ?? "text/plain",
            HostProtocol.TryGetString(payload, HostProtocol.DataField) ?? "");

    // --- variable exchange ---

    /// <summary>
    /// Build the set of store values to place in the Python scope. Values whose serialized form
    /// has not changed since they were last sent are left out: re-injecting them would overwrite
    /// whatever the cell did with the name, which is the behavior the in-process kernel had.
    /// </summary>
    private JsonObject BuildInjectPayload(IVariableStore store)
    {
        var payload = new JsonObject();
        if (!_options.InjectVariables)
            return payload;

        var perVariable = _options.VariableInjectLimitBytes > 0
            ? _options.VariableInjectLimitBytes
            : PythonKernelOptions.DefaultVariableInjectLimitBytes;
        var remaining = (long)perVariable * 4;

        foreach (var descriptor in store.GetAll())
        {
            if (!IsInjectable(descriptor.Name))
                continue;
            if (descriptor.Value is null)
                continue;

            JsonNode? node;
            if (HostVariables.TryToJson(descriptor.Value, out var converted, out var reason))
            {
                if (converted is null)
                    continue;

                node = converted;
                if (_unavailable.Remove(descriptor.Name))
                    _statusChanges.Add((descriptor.Name, null));
            }
            else
            {
                // The name is still bound, carrying the reason instead of the value. Leaving it
                // out was what produced a bare NameError beside a variables pane that listed it.
                node = WireTag.Node(WireTag.Unavailable, reason ?? "it has no JSON form");
                if (!_unavailable.TryGetValue(descriptor.Name, out var told) || told != reason)
                {
                    _unavailable[descriptor.Name] = reason ?? "";
                    _statusChanges.Add((descriptor.Name, reason));
                }
            }

            // Measured once the value is built, and charged against both a per-variable ceiling
            // and what is left for the payload as a whole. Sending more than the transport will
            // carry surfaces as a lost subprocess rather than as the size problem it is.
            var size = node.ToJsonString().Length;
            if (size > perVariable || size > remaining)
            {
                var explanation = size > perVariable
                    ? $"larger than the {perVariable} byte limit for a shared value"
                    : "past what is left of this cell's sharing budget";

                node = WireTag.Node(WireTag.Unavailable, explanation);
                if (!_unavailable.TryGetValue(descriptor.Name, out var told) || told != explanation)
                {
                    _unavailable[descriptor.Name] = explanation;
                    _statusChanges.Add((descriptor.Name, explanation));
                }
            }
            else
            {
                remaining -= size;
            }

            var hash = HostVariables.ComputeHash(node);
            if (_injectedHashes.TryGetValue(descriptor.Name, out var previous) && previous == hash)
                continue;

            payload[descriptor.Name] = node;
            _injectedHashes[descriptor.Name] = hash;
        }

        return payload;
    }

    /// <summary>
    /// Say once, per name, that a variable could not be shared and why. A name is mentioned again
    /// only when its answer changes, so a cell that keeps referring to the same unshareable value
    /// is not narrated every time, and a workaround the author bound for that name is not
    /// contradicted by a later, unrelated cell.
    /// </summary>
    private async Task ReportUnavailableAsync(IExecutionContext context, List<CellOutput> outputs)
    {
        if (_statusChanges.Count == 0)
            return;

        var lines = _statusChanges
            .Where(change => change.Reason is not null)
            .Select(change => $"{change.Name} was not shared with Python because it is {change.Reason}.")
            .ToList();

        _statusChanges.Clear();
        if (lines.Count == 0)
            return;

        lines.Add(
            "Each name is still bound, and using one explains itself. The value remains available "
            + "to the cells in the language that produced it.");

        await EmitAsync(CellOutput.Plain(string.Join(Environment.NewLine, lines)), context, outputs)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether a store name may be placed in the Python scope. A double underscore prefix is the
    /// dividing line: it covers the names the interpreter owns, where binding one would replace
    /// something the scope needs (<c>__builtins__</c> is how a cell reaches every builtin there is),
    /// and it covers Verso's own session keys, which live under a <c>__verso_</c> prefix.
    /// <para>
    /// A single leading underscore is only a convention in Python, and other languages use it for
    /// ordinary variables, so a C# cell binding <c>_rowCount</c> still reaches a Python cell. The
    /// publish side is stricter in the other direction because there the convention does mean
    /// private, and a name Python calls private is not one it intends to share.
    /// </para>
    /// </summary>
    private static bool IsInjectable(string name)
        => !string.IsNullOrEmpty(name) && !name.StartsWith("__", StringComparison.Ordinal);

    private void SnapshotStore(IVariableStore store)
    {
        _preExecutionSnapshot = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var descriptor in store.GetAll())
        {
            if (descriptor.Value is not null)
                _preExecutionSnapshot[descriptor.Name] = descriptor.Value;
        }
    }

    /// <summary>
    /// Whether a store entry was written by something other than this cell while it ran. Such an
    /// entry is left alone: the newer write is the one the user meant.
    /// </summary>
    private bool WasSetDuringExecution(string name, IVariableStore store)
    {
        if (_preExecutionSnapshot is null)
            return false;

        if (!store.TryGet<object>(name, out var current) || current is null)
            return false;

        // Absent from the snapshot but present now: added by something other than this cell.
        if (!_preExecutionSnapshot.TryGetValue(name, out var snapshot))
            return true;

        return !ReferenceEquals(snapshot, current) && !snapshot.Equals(current);
    }

    private async Task PublishVariablesAsync(
        JsonObject reply, IExecutionContext context, List<CellOutput> outputs)
    {
        if (!_options.PublishVariables)
            return;

        if (reply[HostProtocol.VariablesField] is JsonObject variables)
        {
            foreach (var pair in variables)
            {
                if (WasSetDuringExecution(pair.Key, context.Variables))
                    continue;

                // Python reports its whole scope rather than only what the cell touched, so most
                // of what arrives here is a value we injected coming straight back. Storing it
                // would replace a typed value with whatever shape JSON reduced it to: an int
                // published from F# returns as a long, and a later typed read of that name then
                // finds nothing. Identical content means the store already holds the better copy.
                var hash = HostVariables.ComputeHash(pair.Value);
                if (_injectedHashes.TryGetValue(pair.Key, out var injected) && injected == hash)
                    continue;

                var value = HostVariables.FromJson(pair.Value);
                if (value is null)
                    continue;

                context.Variables.Set(pair.Key, value);

                // The value now in the store came from Python, so recording its hash keeps the
                // next cell from injecting the same content straight back.
                _injectedHashes[pair.Key] = hash;
            }
        }

        if (reply[HostProtocol.OversizedField] is not JsonArray oversized || oversized.Count == 0)
            return;

        // Tracked per name rather than once per session, so a second variable running into the
        // limit is still reported instead of being hidden by the first one having been.
        var fresh = oversized
            .Select(node => node?.ToString())
            .Where(name => !string.IsNullOrEmpty(name) && _oversizeReported.Add(name!))
            .ToList();

        if (fresh.Count == 0)
            return;

        var names = string.Join(", ", fresh);
        await EmitAsync(
            CellOutput.Plain(
                $"Some variables were too large to share with other kernels ({names}). " +
                "They remain available in Python; only the shared copy is a short description. " +
                "Raise VariablePublishLimitBytes to share more."),
            context, outputs).ConfigureAwait(false);
    }

    private async Task AppendReplyAsync(JsonObject reply, IExecutionContext context, List<CellOutput> outputs)
    {
        if (reply[HostProtocol.ResultField] is JsonObject result)
            await EmitAsync(MapPayload(result), context, outputs).ConfigureAwait(false);

        if (reply[HostProtocol.ErrorField] is JsonObject error)
        {
            var name = HostProtocol.TryGetString(error, HostProtocol.NameField) ?? "PythonError";
            var traceback = HostProtocol.TryGetString(error, HostProtocol.TracebackField);
            var text = string.IsNullOrEmpty(traceback)
                ? HostProtocol.TryGetString(error, HostProtocol.MessageField) ?? name
                : traceback!;

            await EmitAsync(
                new CellOutput("text/plain", text, IsError: true, ErrorName: name), context, outputs)
                .ConfigureAwait(false);
        }

        await ReportUnavailableAsync(context, outputs).ConfigureAwait(false);
        await PublishVariablesAsync(reply, context, outputs).ConfigureAwait(false);
    }

    private async Task ReportCrashAsync(Exception ex, IExecutionContext context, List<CellOutput> outputs)
    {
        // The connection dropped mid-cell. That is either an interpreter-level crash or a process
        // the operating system took down; either way the stderr tail is the useful evidence.
        var exited = await WaitForExitReportAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var tail = _process?.StandardErrorTail ?? "";

        var message = exited
            ? $"The Python process exited unexpectedly (exit code {_crashExitCode})."
            : $"The Python host connection failed: {ex.Message}";

        if (!string.IsNullOrWhiteSpace(tail))
            message += "\n\nPython output:\n" + tail.TrimEnd();

        _crashed = true;
        await EmitAsync(CellOutput.Error(message, "PythonHostFailure"), context, outputs).ConfigureAwait(false);
    }

    /// <summary>
    /// Wait briefly for the exit to be observed. The socket reaches end of stream as soon as the
    /// subprocess dies, which can be ahead of the exit code being available, and reporting a code
    /// of zero for a crash would be worse than waiting a moment for the real one.
    /// </summary>
    private async Task<bool> WaitForExitReportAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!_crashed && DateTime.UtcNow < deadline)
            await Task.Delay(25).ConfigureAwait(false);

        return _crashed;
    }

    // --- helpers ---

    private static async Task EmitAsync(CellOutput output, IExecutionContext context, List<CellOutput> outputs)
    {
        // Written as it happens so the cell shows progress, and returned as the same instance so the
        // execution pipeline recognizes it and does not append a duplicate at the end.
        outputs.Add(output);
        await context.WriteOutputAsync(output).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows a block of running text, replacing what that block showed before rather than adding
    /// to it. Falls back to appending on a host that cannot revise an output, which yields the
    /// old behaviour of one output per chunk rather than an error.
    /// </summary>
    private async Task ReviseAsync(
        string blockId, CellOutput output, IExecutionContext context, List<CellOutput> outputs)
    {
        if (_blockIndexes.TryGetValue(blockId, out var index) && index < outputs.Count)
        {
            outputs[index] = output;
        }
        else
        {
            _blockIndexes[blockId] = outputs.Count;
            outputs.Add(output);
        }

        try
        {
            await context.UpdateOutputAsync(blockId, output).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // An older host cannot revise an output. Appending each version is noisier than
            // intended but keeps the text visible, which matters more.
            await context.WriteOutputAsync(output).ConfigureAwait(false);
        }
    }

    public static string? NotebookDirectory(IVersoContext context)
    {
        var filePath = context.NotebookMetadata?.FilePath;
        if (string.IsNullOrEmpty(filePath))
            return null;

        try { return Path.GetDirectoryName(Path.GetFullPath(filePath!)); }
        catch { return null; }
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PythonHostSession));
    }
}
