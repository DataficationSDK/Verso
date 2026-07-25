using Python.Runtime;
using Verso.Abstractions;
using Verso.Python.Host;

namespace Verso.Python.Kernel;

/// <summary>
/// Python language kernel for Verso notebooks. Embeds CPython via pythonnet
/// and provides bidirectional variable sharing with other kernels.
/// </summary>
[VersoExtension]
public sealed class PythonKernel : ILanguageKernel, IExtensionSettings
{
    /// <summary>
    /// Set to <c>0</c> or <c>false</c> to run Python inside this process against the embedded
    /// runtime instead of as a separate process against the user's own interpreter. The separate
    /// process is the default; the embedded runtime remains available while it is retired.
    /// </summary>
    internal const string HostOptInVariable = "VERSO_PYTHON_HOST";

    /// <summary>
    /// Selects the install policy: <c>off</c>, <c>prompt</c>, or <c>auto</c>. Delivered through the
    /// environment rather than a notebook setting, because a policy that travelled inside a
    /// notebook file would let a downloaded notebook decide what may install itself.
    /// </summary>
    internal const string AutoInstallVariable = "VERSO_PYTHON_AUTO_INSTALL";

    /// <summary>
    /// Set to <c>off</c> to install and create environments with pip and the standard library
    /// rather than uv, even when uv is on the path.
    /// </summary>
    internal const string UvVariable = "VERSO_PYTHON_UV";

    /// <summary>The notebook setting carrying the requirement list ensured on first execution.</summary>
    internal const string DependenciesSetting = "dependencies";

    private PythonKernelOptions _options;
    private readonly bool _useHostProcess;
    private SemaphoreSlim _executionLock = new(1, 1);
    private PythonScopeManager? _scopeManager;
    private PythonHostSession? _session;
    private string? _sessionInterpreter;
    private bool _initialized;
    private bool _disposed;

    public PythonKernel() : this(new PythonKernelOptions()) { }

    public PythonKernel(PythonKernelOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _useHostProcess = IsHostProcessEnabled();

        // Kernels are constructed by the extension host with default options, so the host's own
        // choices arrive through the environment. An option set explicitly by an embedder wins.
        _options = ApplyEnvironment(_options);
    }

    private static bool IsHostProcessEnabled()
    {
        var value = Environment.GetEnvironmentVariable(HostOptInVariable);
        return value is not ("0" or "false" or "FALSE" or "False");
    }

    private static PythonKernelOptions ApplyEnvironment(PythonKernelOptions options)
    {
        var policy = Environment.GetEnvironmentVariable(AutoInstallVariable);
        if (TryParsePolicy(policy, out var parsed))
            options = options with { AutoInstall = parsed };

        var uv = Environment.GetEnvironmentVariable(UvVariable);
        if (uv is "0" or "off" or "OFF" or "Off" or "false" or "FALSE" or "False")
            options = options with { UseUv = UvUsage.Off };

        return options;
    }

    internal static bool TryParsePolicy(string? value, out AutoInstallPolicy policy)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "off": policy = AutoInstallPolicy.Off; return true;
            case "prompt": policy = AutoInstallPolicy.Prompt; return true;
            case "auto": policy = AutoInstallPolicy.Auto; return true;
            default: policy = AutoInstallPolicy.Prompt; return false;
        }
    }

    /// <summary>
    /// Records the interpreter chosen with <c>#!python</c> for this session. Held on the kernel
    /// because a restart clears the variable store before the kernel starts again, and the
    /// selection has to outlive that.
    /// </summary>
    internal void SetSessionInterpreter(string? executable)
    {
        _sessionInterpreter = executable;
        _session?.SetSessionInterpreter(executable);
    }

    /// <summary>Whether this kernel runs Python out of process.</summary>
    internal bool UsesHostProcess => _useHostProcess;

    /// <summary>
    /// Resolve the interpreter this kernel executes against, binding the session to the notebook
    /// first so a magic command running in the very first cell installs into the same environment
    /// the cell below it will import from. Returns null when Python runs in this process, which
    /// has no separate interpreter to install into.
    /// </summary>
    internal async Task<string?> GetActiveInterpreterAsync(
        IVersoContext context, CancellationToken cancellationToken)
    {
        if (!_useHostProcess)
            return null;

        if (!_initialized)
            await InitializeAsync().ConfigureAwait(false);

        var session = _session;
        if (session is null)
            return null;

        await session.EnsureBoundAsync(PythonHostSession.NotebookDirectory(context), cancellationToken)
            .ConfigureAwait(false);

        return session.Interpreter?.Executable;
    }

    // --- IExtension ---

    public string ExtensionId => "verso.kernel.python";
    public string Name => "Python";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Python language kernel.";

    // --- ILanguageKernel ---

    public string LanguageId => "python";
    public string DisplayName => "Python";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".py" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IExtensionSettings ---

    public IReadOnlyList<SettingDefinition> SettingDefinitions { get; } = new[]
    {
        new SettingDefinition(DependenciesSetting, "Dependencies",
            "Requirement strings installed into the notebook's Python environment before the " +
            "first cell runs, under the same consent policy as an import. One per entry, in the " +
            "form pip accepts (for example \"pandas>=2\").",
            SettingType.StringList, null, "Packages"),
    };

    public IReadOnlyDictionary<string, object?> GetSettingValues()
    {
        var values = new Dictionary<string, object?>();
        if (_options.Dependencies.Count > 0)
            values[DependenciesSetting] = _options.Dependencies.ToArray();
        return values;
    }

    public Task ApplySettingsAsync(IReadOnlyDictionary<string, object?> values)
    {
        if (values.TryGetValue(DependenciesSetting, out var value))
            SetDependencies(ReadStringList(value));

        return Task.CompletedTask;
    }

    public Task OnSettingChangedAsync(string name, object? value)
    {
        if (string.Equals(name, DependenciesSetting, StringComparison.Ordinal))
            SetDependencies(ReadStringList(value));

        return Task.CompletedTask;
    }

    private void SetDependencies(IReadOnlyList<string> dependencies)
    {
        _options = _options with { Dependencies = dependencies };

        // A running session holds its own copy so a settings change that lands after
        // initialization still takes effect on the next cell.
        _session?.AutoInstall.UpdateOptions(_options);
    }

    /// <summary>
    /// Read a list setting, which arrives as a string array from the settings UI and as a
    /// deserialized JSON array from a notebook file.
    /// </summary>
    private static IReadOnlyList<string> ReadStringList(object? value)
    {
        if (value is null)
            return Array.Empty<string>();

        // A settings surface that has no list editor sends one comma-separated string, which is
        // also the most natural thing for a person to type.
        if (value is string single)
        {
            return single
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (value is not System.Collections.IEnumerable items)
            return Array.Empty<string>();

        var results = new List<string>();
        foreach (var item in items)
        {
            var text = item?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                results.Add(text!.Trim());
        }

        return results;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        // Support re-initialization after disposal (kernel restart)
        _disposed = false;

        if (_useHostProcess)
        {
            await InitializeHostSessionAsync().ConfigureAwait(false);
            return;
        }

        PythonEngineManager.EnsureInitialized(_options.PythonDll);

        // Create scope and run bootstrap on a thread pool thread with the GIL
        await Task.Run(() =>
        {
            using (Py.GIL())
            {
                _scopeManager = new PythonScopeManager();
                _scopeManager.Initialize();

                // Execute output capture bootstrap
                _scopeManager.Exec(OutputCapture.BootstrapCode);

                // Execute default imports
                foreach (var import in _options.DefaultImports)
                {
                    try
                    {
                        _scopeManager.Exec($"import {import}");
                    }
                    catch (PythonException)
                    {
                        // Skip unavailable modules silently
                    }
                }

                // Execute optional startup code
                if (!string.IsNullOrWhiteSpace(_options.StartupCode))
                {
                    _scopeManager.Exec(_options.StartupCode);
                }
            }

            _executionLock = new SemaphoreSlim(1, 1);
            _initialized = true;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the out-of-process host. Kernel initialization carries no notebook context, so the
    /// interpreter is resolved against the process working directory and the first execution
    /// rebinds to the notebook's own directory if they differ. A startup failure is not thrown
    /// here: it is reported as cell output on the first execution, where the user can see it.
    /// </summary>
    private async Task InitializeHostSessionAsync()
    {
        _session = new PythonHostSession(_options, _sessionInterpreter);
        _executionLock = new SemaphoreSlim(1, 1);
        _initialized = true;

        try
        {
            await _session.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The session restarts on demand and surfaces the reason as an error output.
        }
    }

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(code))
            return Array.Empty<CellOutput>();

        if (_useHostProcess)
        {
            await _executionLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
            try
            {
                return await _session!.ExecuteAsync(code, context).ConfigureAwait(false);
            }
            finally
            {
                _executionLock.Release();
            }
        }

        await _executionLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ExecuteWithGil(code, context), context.CancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _executionLock.Release();
        }
    }

    // Editor assistance is answered by the Python process itself, so it sees the interpreter the
    // cells run in and the names they defined. The embedded runtime does not provide it.

    public async Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        if (_session is null)
            return Array.Empty<Completion>();

        try
        {
            return await _session.GetCompletionsAsync(code, cursorPosition).ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<Completion>();
        }
    }

    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        if (_session is null)
            return Array.Empty<Diagnostic>();

        try
        {
            return await _session.GetDiagnosticsAsync(code).ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<Diagnostic>();
        }
    }

    public async Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        if (_session is null)
            return null;

        try
        {
            return await _session.GetHoverInfoAsync(code, cursorPosition).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _initialized = false;

        if (_session is not null)
        {
            var session = _session;
            _session = null;
            return session.DisposeAsync();
        }

        if (_scopeManager is not null)
        {
            // Dispose scope under GIL on a thread-pool thread
            var scope = _scopeManager;
            _scopeManager = null;

            return new ValueTask(Task.Run(() =>
            {
                using (Py.GIL())
                {
                    scope.Dispose();
                }
            }));
        }

        _executionLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<CellOutput> ExecuteWithGil(string code, IExecutionContext context)
    {
        using (Py.GIL())
        {
            var outputs = new List<CellOutput>();

            try
            {
                // Inject shared variables into Python scope
                if (_options.InjectVariables)
                {
                    _scopeManager!.InjectFromStore(context.Variables);
                }

                // Add package paths to sys.path if #!pip has been used.
                // The store value may contain multiple paths separated by Path.PathSeparator
                // (on Windows: venv site-packages + overlay directory).
                if (context.Variables.TryGet<string>(VenvManager.SitePackagesStoreKey, out var sitePackages)
                    && !string.IsNullOrEmpty(sitePackages))
                {
                    foreach (var sp in sitePackages.Split(Path.PathSeparator))
                    {
                        if (string.IsNullOrEmpty(sp)) continue;
                        var escaped = sp.Replace("\\", "\\\\").Replace("'", "\\'");
                        _scopeManager!.Exec(
                            $"if '{escaped}' not in sys.path: sys.path.insert(0, '{escaped}')");
                    }

                    // Re-run library hooks (matplotlib Agg backend, IPython shims) so
                    // packages installed via #!pip are configured before user code runs.
                    _scopeManager!.Exec(OutputCapture.LibraryHooksCode);
                }

                // Reset output buffers
                _scopeManager!.Exec(
                    "_verso_stdout = io.StringIO()\n" +
                    "_verso_stderr = io.StringIO()\n" +
                    "sys.stdout = _verso_stdout\n" +
                    "sys.stderr = _verso_stderr\n" +
                    "_verso_display_outputs.clear()");

                // Snapshot the store just before user code runs so the post-execution publish can
                // preserve any value an external actor (e.g. a layout interaction) writes while the
                // cell is executing.
                if (_options.InjectVariables)
                    _scopeManager!.SnapshotStore(context.Variables);

                // Execute user code
                _scopeManager.Exec(code);

                // Capture any open matplotlib figures not already captured by plt.show().
                // Covers cases where the user creates plots without calling show().
                _scopeManager.Exec(OutputCapture.MatplotlibCaptureCode);

                // Drain stdout
                using var stdoutResult = _scopeManager.Eval("_verso_flush_stdout()");
                var stdout = stdoutResult.ToString() ?? "";
                if (!string.IsNullOrEmpty(stdout))
                {
                    outputs.Add(new CellOutput("text/plain", stdout));
                }

                // Drain stderr
                using var stderrResult = _scopeManager.Eval("_verso_flush_stderr()");
                var stderr = stderrResult.ToString() ?? "";
                if (!string.IsNullOrEmpty(stderr))
                {
                    outputs.Add(new CellOutput("text/plain", stderr, IsError: true));
                }

                // Drain display queue and write outputs immediately
                using var displayItems = _scopeManager.Eval("_verso_flush_display()");
                using var pyList = new PyList(displayItems);
                for (var i = 0; i < pyList.Length(); i++)
                {
                    using var item = pyList[i];
                    using var mimeObj = item[0];
                    using var contentObj = item[1];
                    var mime = mimeObj.ToString() ?? "text/plain";
                    var content = contentObj.ToString() ?? "";
                    var cellOutput = new CellOutput(mime, content);
                    context.WriteOutputAsync(cellOutput).GetAwaiter().GetResult();
                    outputs.Add(cellOutput);
                }

                // Try last-expression capture: compile the last line as an eval expression
                TryCaptureLastExpression(code, outputs);

                // Publish Python locals to shared variable store
                if (_options.PublishVariables)
                {
                    _scopeManager.PublishToStore(context.Variables);
                }
            }
            catch (PythonException ex)
            {
                var errorOutput = FormatPythonException(ex);
                outputs.Add(errorOutput);
            }

            return outputs;
        }
    }

    private void TryCaptureLastExpression(string code, List<CellOutput> outputs)
    {
        try
        {
            // Get the last non-empty, non-comment line
            var lines = code.Split('\n');
            var lastLine = "";
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var trimmed = lines[i].Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                {
                    lastLine = trimmed;
                    break;
                }
            }

            if (string.IsNullOrEmpty(lastLine)) return;

            // Skip lines that are clearly statements
            if (lastLine.StartsWith("import ") || lastLine.StartsWith("from ")
                || lastLine.StartsWith("def ") || lastLine.StartsWith("class ")
                || lastLine.StartsWith("if ") || lastLine.StartsWith("for ")
                || lastLine.StartsWith("while ") || lastLine.StartsWith("with ")
                || lastLine.StartsWith("try:") || lastLine.StartsWith("except")
                || lastLine.StartsWith("print(") || lastLine.StartsWith("print (")
                || lastLine.Contains("=") && !lastLine.Contains("=="))
                return;

            // Try to compile as eval; if it fails, it's not an expression
            _scopeManager!.Exec($"_verso_last_expr_check = compile({EscapePythonString(lastLine)}, '<expr>', 'eval')");
            using var result = _scopeManager.Eval(lastLine);

            if (result is not null && !result.IsNone())
            {
                // Try rich display first
                string? displayContent = null;
                string mime = "text/plain";

                // Check _repr_html_
                if (result.HasAttr("_repr_html_"))
                {
                    try
                    {
                        using var html = result.InvokeMethod("_repr_html_");
                        if (!html.IsNone())
                        {
                            displayContent = html.ToString();
                            mime = "text/html";
                        }
                    }
                    catch { /* fall through */ }
                }

                displayContent ??= result.InvokeMethod("__repr__").ToString();

                if (!string.IsNullOrEmpty(displayContent))
                {
                    outputs.Add(new CellOutput(mime, displayContent));
                }
            }
        }
        catch
        {
            // Not a valid expression, skip silently
        }
        finally
        {
            // Clean up the compile check variable
            try { _scopeManager!.Exec("del _verso_last_expr_check"); } catch { /* ignore */ }
        }
    }

    private static CellOutput FormatPythonException(PythonException ex)
    {
        // Map KeyboardInterrupt to OperationCanceledException
        if (ex.Type?.Name == "KeyboardInterrupt")
        {
            throw new OperationCanceledException("Python execution was cancelled.", ex);
        }

        var message = ex.Message;
        var traceback = ex.StackTrace;

        // Try to get a better traceback from Python
        try
        {
            using (Py.GIL())
            {
                using var tb = Py.Import("traceback");
                using var formatted = tb.InvokeMethod("format_exception",
                    ex.Type ?? PyObject.None,
                    ex.Value ?? PyObject.None,
                    ex.Traceback ?? PyObject.None);
                using var joined = PyObject.None;
                var tbStr = string.Join("", formatted.As<string[]>());
                if (!string.IsNullOrEmpty(tbStr))
                {
                    message = tbStr;
                    traceback = null; // already included in the formatted message
                }
            }
        }
        catch
        {
            // Fall back to basic exception info
        }

        return new CellOutput(
            "text/plain",
            message,
            IsError: true,
            ErrorName: ex.Type?.Name ?? "PythonException",
            ErrorStackTrace: traceback);
    }

    private static string EscapePythonString(string value)
    {
        return "'''" + value.Replace("\\", "\\\\").Replace("'''", "\\'\\'\\'") + "'''";
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel has not been initialized. Call InitializeAsync first.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PythonKernel));
    }
}
