using Verso.Abstractions;
using Verso.JavaScript.MagicCommands;

namespace Verso.JavaScript.Kernel;

/// <summary>
/// TypeScript language kernel for Verso notebooks. Transpiles TypeScript to JavaScript
/// using the TypeScript compiler API, then executes via the same Node.js subprocess
/// used by the JavaScript kernel.
/// </summary>
/// <remarks>
/// Requires Node.js and the <c>typescript</c> npm module. If typescript is not installed,
/// the kernel will auto-install it on first use. Jint fallback is not supported for TypeScript.
/// </remarks>
[VersoExtension]
public sealed class TypeScriptKernel : ILanguageKernel
{
    private readonly JavaScriptKernelOptions _options;
    private SemaphoreSlim _executionLock = new(1, 1);
    private IJavaScriptRunner? _runner;
    private VariableBridge? _variableBridge;
    private bool _initialized;
    private bool _disposed;
    private bool _typescriptInstalled;

    public TypeScriptKernel() : this(new JavaScriptKernelOptions()) { }

    public TypeScriptKernel(JavaScriptKernelOptions options) => _options = options;

    // IExtension
    public string ExtensionId => "verso.kernel.typescript";
    public string Name => "TypeScript";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "TypeScript language kernel via Node.js with automatic transpilation.";

    // ILanguageKernel
    public string LanguageId => "typescript";
    public string DisplayName => "TypeScript";
    public IReadOnlyList<string> FileExtensions { get; } = [".ts", ".tsx"];

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _disposed = false;

        var nodeExe = _options.NodeExecutablePath ?? JavaScriptEngineManager.NodeExecutablePath;
        if (nodeExe is null)
            throw new InvalidOperationException(
                "TypeScript kernel requires Node.js. Node.js was not found on PATH or in well-known locations.");

        _runner = new NodeProcessRunner(nodeExe, _options);
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
        => Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
        => Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
        => Task.FromResult<HoverInfo?>(null);

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
        if (!_runner!.IsAlive && _options.AutoRestartOnCrash)
        {
            outputs.Add(new CellOutput("text/plain", "Node.js process crashed. Restarting..."));
            await context.WriteOutputAsync(outputs[0]);

            await _runner.DisposeAsync();
            var nodeExe = _options.NodeExecutablePath ?? JavaScriptEngineManager.NodeExecutablePath;
            _runner = new NodeProcessRunner(nodeExe!, _options);
            await _runner.InitializeAsync(ct);
            _typescriptInstalled = false;
        }

        // Auto-install typescript if needed
        if (!_typescriptInstalled)
        {
            await EnsureTypeScriptInstalledAsync(context, ct);
            _typescriptInstalled = true;
        }

        // Inject variables from store
        if (_options.InjectVariables)
        {
            try { await _variableBridge!.InjectFromStore(context.Variables, _runner!, ct); }
            catch { }
        }

        // Update NODE_PATH if npm packages were installed
        if (_runner is NodeProcessRunner nodeRunner
            && context.Variables.TryGet<string>(NpmManager.NodePathStoreKey, out var nodePath)
            && nodePath is not null)
        {
            try { await nodeRunner.AddModulePathAsync(nodePath, ct); }
            catch { }
        }

        // Transpile TypeScript to JavaScript
        TranspileResult transpileResult;
        try
        {
            transpileResult = await _runner!.TranspileAsync(code, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return [new CellOutput("text/plain", $"Transpilation failed: {ex.Message}",
                IsError: true, ErrorName: "TypeScriptError")];
        }

        if (!transpileResult.Success)
        {
            return [new CellOutput("text/plain", transpileResult.Error ?? "Unknown transpilation error",
                IsError: true, ErrorName: "TypeScriptError")];
        }

        // Execute the transpiled JavaScript
        JavaScriptRunResult result;
        try
        {
            result = await _runner!.ExecuteAsync(transpileResult.Code!, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return [new CellOutput("text/plain", ex.Message, IsError: true, ErrorName: "TypeScriptKernelError")];
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
                ErrorName: "TypeScriptError",
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

    private async Task EnsureTypeScriptInstalledAsync(IExecutionContext context, CancellationToken ct)
    {
        // Quick check: try transpiling a trivial expression
        var probe = await _runner!.TranspileAsync("const _: number = 1;", ct);
        if (probe.Success) return;

        // TypeScript not available, auto-install silently
        if (!NpmManager.IsPackageInstalled("typescript"))
        {
            await NpmManager.EnsureInitializedAsync(ct);
            var success = await NpmManager.InstallSilentAsync("typescript", ct);
            if (!success)
                throw new InvalidOperationException("Failed to install the typescript npm package.");
        }

        // Update NODE_PATH so the bridge can find the module
        var nodeModulesPath = NpmManager.NodeModulesPath;
        context.Variables.Set(NpmManager.NodePathStoreKey, nodeModulesPath);

        if (_runner is NodeProcessRunner nodeRunner)
            nodeRunner.UpdateNodeModulesPath(nodeModulesPath);

        // The typescript module won't be requireable until NODE_PATH is updated.
        // Restart the Node process so it picks up the new NODE_PATH.
        await _runner.DisposeAsync();
        var nodeExe = _options.NodeExecutablePath ?? JavaScriptEngineManager.NodeExecutablePath;
        _runner = new NodeProcessRunner(nodeExe!, _options);

        if (_runner is NodeProcessRunner newRunner)
            newRunner.UpdateNodeModulesPath(nodeModulesPath);

        await _runner.InitializeAsync(ct);

        // Verify
        var verify = await _runner.TranspileAsync("const _: number = 1;", ct);
        if (!verify.Success)
            throw new InvalidOperationException($"TypeScript compiler not working after install: {verify.Error}");
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
            await InitializeAsync();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TypeScriptKernel));
    }
}
