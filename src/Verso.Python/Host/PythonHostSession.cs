using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Verso.Abstractions;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;

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

    private PythonHostProcess? _process;
    private string _executable = "";
    private string _workingDirectory = "";
    private string? _sessionSelection;
    private bool _boundToNotebook;
    private int _crashExitCode;
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
        _bootstrap = new HostBootstrap(options.DefaultImports, options.StartupCode);
    }

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

        var request = new JsonObject
        {
            [HostProtocol.TypeField] = HostProtocol.Execute,
            [HostProtocol.CodeField] = code,
            // Variable exchange rides on these fields; it is wired up with the rich display work.
            [HostProtocol.InjectField] = new JsonObject(),
            [HostProtocol.PublishField] = false,
        };

        var events = Channel.CreateUnbounded<JsonObject>(
            new UnboundedChannelOptions { SingleReader = true });

        void OnEvent(JsonObject message) => events.Writer.TryWrite(message);
        connection.EventReceived += OnEvent;

        try
        {
            // Not cancelled through the token: interrupt escalation below decides when to give up
            // on a reply, and a cancelled registration would lose the result the host still sends.
            var reply = connection.SendRequestAsync(request, CancellationToken.None);
            var consumer = ConsumeEventsAsync(events.Reader, context, outputs);

            var completedNormally = await AwaitReplyAsync(reply, process, context, outputs).ConfigureAwait(false);

            events.Writer.TryComplete();
            await consumer.ConfigureAwait(false);

            if (completedNormally)
            {
                await AppendReplyAsync(await reply.ConfigureAwait(false), context, outputs).ConfigureAwait(false);
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
            connection.EventReceived -= OnEvent;
        }

        return outputs;
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

        var process = new PythonHostProcess(executable, workingDirectory, _bootstrap);
        process.Exited += OnProcessExited;

        await process.StartAsync(cancellationToken).ConfigureAwait(false);

        _process = process;
        _executable = executable;
        _workingDirectory = workingDirectory;
        _crashed = false;
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
    private async Task PrepareForExecutionAsync(IExecutionContext context, List<CellOutput> outputs)
    {
        var cancellationToken = context.CancellationToken;

        if (!_boundToNotebook)
        {
            // Initialization ran without a notebook, so this is the first chance to honour the
            // notebook's own directory and any interpreter chosen for the session. Rebinding is
            // free here: no cell has run, so there is no interpreter state to lose.
            var directory = NotebookDirectory(context) ?? Environment.CurrentDirectory;
            var executable = await ResolveExecutableAsync(directory, cancellationToken).ConfigureAwait(false);

            // Only marked once resolution succeeded, so a failure here is retried on the next run
            // rather than leaving the session bound to an interpreter it never found.
            _boundToNotebook = true;

            if (_process is null || !PathsEqual(directory, _workingDirectory) || !PathsEqual(executable, _executable))
            {
                await StartProcessAsync(executable, directory, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

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

                var isError = HostProtocol.TryGetString(message, HostProtocol.NameField) == HostProtocol.StreamStderr;
                await EmitAsync(new CellOutput("text/plain", text!, IsError: isError), context, outputs)
                    .ConfigureAwait(false);
                return;
            }

            case HostProtocol.Display:
            {
                var mime = HostProtocol.TryGetString(message, HostProtocol.MimeField) ?? "text/plain";
                var data = HostProtocol.TryGetString(message, HostProtocol.DataField) ?? "";
                await EmitAsync(new CellOutput(mime, data), context, outputs).ConfigureAwait(false);
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

    private async Task AppendReplyAsync(JsonObject reply, IExecutionContext context, List<CellOutput> outputs)
    {
        if (reply[HostProtocol.ResultField] is JsonObject result)
        {
            var mime = HostProtocol.TryGetString(result, HostProtocol.MimeField) ?? "text/plain";
            var data = HostProtocol.TryGetString(result, HostProtocol.DataField) ?? "";
            await EmitAsync(new CellOutput(mime, data), context, outputs).ConfigureAwait(false);
        }

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

    private static string? NotebookDirectory(IExecutionContext context)
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
