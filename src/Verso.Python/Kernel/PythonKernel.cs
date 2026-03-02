using Python.Runtime;
using Verso.Abstractions;

namespace Verso.Python.Kernel;

/// <summary>
/// Python language kernel for Verso notebooks. Embeds CPython via pythonnet
/// and provides bidirectional variable sharing with other kernels.
/// </summary>
[VersoExtension]
public sealed class PythonKernel : ILanguageKernel
{
    private readonly PythonKernelOptions _options;
    private SemaphoreSlim _executionLock = new(1, 1);
    private PythonScopeManager? _scopeManager;
    private PythonCompletionProvider? _completionProvider;
    private bool _initialized;
    private bool _disposed;

    public PythonKernel() : this(new PythonKernelOptions()) { }

    public PythonKernel(PythonKernelOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // --- IExtension ---

    public string ExtensionId => "verso.kernel.python";
    public string Name => "Python (pythonnet)";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Python language kernel powered by pythonnet.";

    // --- ILanguageKernel ---

    public string LanguageId => "python";
    public string DisplayName => "Python";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".py" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        // Support re-initialization after disposal (kernel restart)
        _disposed = false;

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

        // Initialize IntelliSense provider
        _completionProvider = new PythonCompletionProvider(_scopeManager!);
        await _completionProvider.ProbeJediAsync().ConfigureAwait(false);

        if (!_completionProvider.JediAvailable)
        {
            if (await VenvManager.EnsureJediInstalledAsync(CancellationToken.None).ConfigureAwait(false))
            {
                // Add venv site-packages to sys.path so jedi is importable
                var sitePackages = await VenvManager.GetSitePackagesPathAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                if (sitePackages is not null)
                {
                    await Task.Run(() =>
                    {
                        using (Py.GIL())
                        {
                            var escaped = sitePackages.Replace("\\", "\\\\").Replace("'", "\\'");
                            _scopeManager!.Exec(
                                $"import sys\n" +
                                $"if '{escaped}' not in sys.path: sys.path.insert(0, '{escaped}')");
                        }
                    }).ConfigureAwait(false);
                }

                await _completionProvider.ProbeJediAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(code))
            return Array.Empty<CellOutput>();

        await _executionLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            var outputs = await Task.Run(() => ExecuteWithGil(code, context), context.CancellationToken)
                .ConfigureAwait(false);

            // Track successfully executed code for cross-cell IntelliSense context
            _completionProvider?.AppendExecutedCode(code);

            return outputs;
        }
        finally
        {
            _executionLock.Release();
        }
    }

    public async Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        if (_completionProvider is null)
            return Array.Empty<Completion>();

        try
        {
            return await _completionProvider.GetCompletionsAsync(code, cursorPosition).ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<Completion>();
        }
    }

    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        if (_completionProvider is null)
            return Array.Empty<Diagnostic>();

        try
        {
            return await _completionProvider.GetDiagnosticsAsync(code).ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<Diagnostic>();
        }
    }

    public async Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        if (_completionProvider is null)
            return null;

        try
        {
            return await _completionProvider.GetHoverInfoAsync(code, cursorPosition).ConfigureAwait(false);
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

        _completionProvider?.Dispose();
        _completionProvider = null;

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

                // Add venv site-packages to sys.path if #!pip has been used
                if (context.Variables.TryGet<string>(VenvManager.SitePackagesStoreKey, out var sitePackages)
                    && !string.IsNullOrEmpty(sitePackages))
                {
                    var escaped = sitePackages.Replace("\\", "\\\\").Replace("'", "\\'");
                    _scopeManager!.Exec(
                        $"_verso_sp_added = '{escaped}' not in sys.path\n" +
                        $"if _verso_sp_added: sys.path.insert(0, '{escaped}')");

                    // Re-run library hooks (matplotlib Agg backend, IPython shims) so
                    // packages installed via #!pip are configured before user code runs.
                    _scopeManager.Exec(OutputCapture.LibraryHooksCode);
                }

                // Reset output buffers
                _scopeManager!.Exec(
                    "_verso_stdout = io.StringIO()\n" +
                    "_verso_stderr = io.StringIO()\n" +
                    "sys.stdout = _verso_stdout\n" +
                    "sys.stderr = _verso_stderr\n" +
                    "_verso_display_outputs.clear()");

                // Execute user code
                _scopeManager.Exec(code);

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

                // Drain display queue
                using var displayItems = _scopeManager.Eval("_verso_flush_display()");
                using var pyList = new PyList(displayItems);
                for (var i = 0; i < pyList.Length(); i++)
                {
                    using var item = pyList[i];
                    using var mimeObj = item[0];
                    using var contentObj = item[1];
                    var mime = mimeObj.ToString() ?? "text/plain";
                    var content = contentObj.ToString() ?? "";
                    outputs.Add(new CellOutput(mime, content));
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
