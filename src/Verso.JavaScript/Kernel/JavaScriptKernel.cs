using Verso.Abstractions;
using Verso.JavaScript.MagicCommands;

namespace Verso.JavaScript.Kernel;

/// <summary>
/// JavaScript language kernel for Verso notebooks. Uses Node.js (subprocess) when available,
/// with Jint (pure .NET) as an in-process fallback.
/// </summary>
[VersoExtension]
public sealed class JavaScriptKernel : ILanguageKernel
{
    private readonly JavaScriptKernelOptions _options;
    private SemaphoreSlim _executionLock = new(1, 1);
    private IJavaScriptRunner? _runner;
    private VariableBridge? _variableBridge;
    private bool _initialized;
    private bool _disposed;
    private bool _usingNode;

    public JavaScriptKernel() : this(new JavaScriptKernelOptions()) { }

    public JavaScriptKernel(JavaScriptKernelOptions options) => _options = options;

    // IExtension
    public string ExtensionId => "verso.kernel.javascript";
    public string Name => "JavaScript";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "JavaScript language kernel via Node.js or Jint.";

    // ILanguageKernel
    public string LanguageId => "javascript";
    public string DisplayName => "JavaScript";
    public IReadOnlyList<string> FileExtensions { get; } = [".js", ".mjs"];

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _disposed = false;

        var nodeExe = _options.NodeExecutablePath ?? JavaScriptEngineManager.NodeExecutablePath;
        if (nodeExe is not null && !_options.ForceJint)
        {
            _runner = new NodeProcessRunner(nodeExe, _options);
            _usingNode = true;
        }
        else
        {
            _runner = new JintRunner(_options);
            _usingNode = false;
        }

        await _runner.InitializeAsync(CancellationToken.None);

        if (_options.StartupCode is not null)
            await _runner.ExecuteAsync(_options.StartupCode, CancellationToken.None);

        _variableBridge = new VariableBridge(_options);
        _executionLock = new SemaphoreSlim(1, 1);
        _initialized = true;
    }

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync();

        if (string.IsNullOrWhiteSpace(code))
            return Array.Empty<CellOutput>();

        await _executionLock.WaitAsync(context.CancellationToken);
        try
        {
            return await ExecuteCoreAsync(code, context);
        }
        finally
        {
            _executionLock.Release();
        }
    }

    public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        return Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());
    }

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        return Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());
    }

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        return Task.FromResult<HoverInfo?>(null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _initialized = false;

        if (_runner is not null)
        {
            var r = _runner;
            _runner = null;
            await r.DisposeAsync();
        }

        _variableBridge = null;
        _executionLock.Dispose();
    }

    private async Task<IReadOnlyList<CellOutput>> ExecuteCoreAsync(string code, IExecutionContext context)
    {
        var ct = context.CancellationToken;
        var outputs = new List<CellOutput>();

        // Crash recovery
        if (!_runner!.IsAlive && _usingNode && _options.AutoRestartOnCrash)
        {
            outputs.Add(new CellOutput("text/plain", "Node.js process crashed. Restarting..."));
            await context.WriteOutputAsync(outputs[0]);

            await _runner.DisposeAsync();
            var nodeExe = _options.NodeExecutablePath ?? JavaScriptEngineManager.NodeExecutablePath;
            _runner = new NodeProcessRunner(nodeExe!, _options);
            await _runner.InitializeAsync(ct);
        }

        // Inject variables from store
        if (_options.InjectVariables)
        {
            try { await _variableBridge!.InjectFromStore(context.Variables, _runner!, ct); }
            catch { }
        }

        // Update NODE_PATH if npm packages were installed
        if (_usingNode
            && _runner is NodeProcessRunner nodeRunner
            && context.Variables.TryGet<string>(NpmManager.NodePathStoreKey, out var nodePath)
            && nodePath is not null)
        {
            try { await nodeRunner.AddModulePathAsync(nodePath, ct); }
            catch { }
        }

        // Execute
        JavaScriptRunResult result;
        try
        {
            result = await _runner!.ExecuteAsync(code, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return [new CellOutput("text/plain", ex.Message, IsError: true, ErrorName: "JavaScriptKernelError")];
        }

        // Build outputs
        if (result.Stdout is not null)
            outputs.Add(new CellOutput("text/plain", result.Stdout));

        if (result.Stderr is not null)
            outputs.Add(new CellOutput("text/plain", result.Stderr, IsError: true));

        if (result.HasError)
        {
            outputs.Add(new CellOutput(
                "text/plain",
                result.ErrorMessage ?? "Unknown JavaScript error",
                IsError: true,
                ErrorName: "JavaScriptError",
                ErrorStackTrace: result.ErrorStack));
        }
        else if (result.LastExpressionJson is not null)
        {
            outputs.Add(new CellOutput("application/json", result.LastExpressionJson));
        }

        // Publish variables back to store
        if (_options.PublishVariables && !result.HasError)
        {
            try { await _variableBridge!.PublishToStore(context.Variables, _runner!, result.UserGlobals, ct); }
            catch { }
        }

        return outputs;
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
            await InitializeAsync();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(JavaScriptKernel));
    }
}
