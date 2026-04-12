using Verso.Abstractions;

namespace Verso.PowerShell.Kernel;

[VersoExtension]
public sealed class PowerShellKernel : ILanguageKernel
{
    private PowerShellKernelOptions _options = new();
    private SemaphoreSlim _executionLock = new(1, 1);
    private RunspaceManager? _runspaceManager;
    private VariableBridge? _variableBridge;
    private bool _variablesInjected;
    private bool _initialized;
    private bool _disposed;

    // IExtension
    public string ExtensionId => "verso.powershell.kernel";
    public string Name => "PowerShell";
    public string Version => "1.0.0";
    public string? Author => "Datafication";
    public string? Description => "PowerShell language kernel powered by System.Management.Automation.";

    // ILanguageKernel
    public string LanguageId => "powershell";
    public string DisplayName => "PowerShell";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".ps1", ".psm1" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;

        _disposed = false;

        _runspaceManager = new RunspaceManager();
        _runspaceManager.Initialize();

        // Inject Display function that routes through DisplayContext
        _runspaceManager.InjectDisplayFunction();

        _variableBridge = new VariableBridge(_runspaceManager, _options);
        _variablesInjected = false;

        _executionLock = new SemaphoreSlim(1, 1);
        _initialized = true;

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(code))
            return Array.Empty<CellOutput>();

        await _executionLock.WaitAsync(context.CancellationToken);
        try
        {
            // Inject variables from store on first execution
            if (!_variablesInjected)
            {
                _variableBridge!.InjectFromStore(context.Variables);
                _variablesInjected = true;
            }
            else
            {
                // Re-inject on subsequent executions to pick up changes from other kernels
                _variableBridge!.InjectFromStore(context.Variables);
            }

            var result = _runspaceManager!.Invoke(code, context.CancellationToken);
            var outputs = new List<CellOutput>();

            // Output stream (objects)
            if (result.OutputLines.Count > 0)
            {
                var text = string.Join(Environment.NewLine, result.OutputLines);
                if (!string.IsNullOrEmpty(text))
                    outputs.Add(new CellOutput(result.OutputMimeType, text));
            }

            // Information stream (Write-Host)
            if (result.InformationLines.Count > 0)
            {
                var text = string.Join(Environment.NewLine, result.InformationLines);
                if (!string.IsNullOrEmpty(text))
                    outputs.Add(new CellOutput("text/plain", text));
            }

            // Warning stream
            if (result.WarningLines.Count > 0)
            {
                var text = string.Join(Environment.NewLine,
                    result.WarningLines.Select(w => $"[WARNING] {w}"));
                outputs.Add(new CellOutput("text/plain", text));
            }

            // Error stream
            if (result.ErrorLines.Count > 0)
            {
                var text = string.Join(Environment.NewLine, result.ErrorLines);
                outputs.Add(new CellOutput("text/plain", text,
                    IsError: true,
                    ErrorName: "PSError",
                    ErrorStackTrace: result.Exception?.StackTrace));
            }

            // Publish variables after execution
            _variableBridge!.PublishToStore(context.Variables);

            return outputs;
        }
        finally
        {
            _executionLock.Release();
        }
    }

    public async Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        ThrowIfDisposed();
        if (!_initialized) return Array.Empty<Completion>();

        var acquired = await _executionLock.WaitAsync(TimeSpan.FromSeconds(5));
        if (!acquired) return Array.Empty<Completion>();

        try
        {
            return _runspaceManager!.GetCompletions(code, cursorPosition);
        }
        catch
        {
            return Array.Empty<Completion>();
        }
        finally
        {
            _executionLock.Release();
        }
    }

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        ThrowIfDisposed();
        if (!_initialized) return Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());

        try
        {
            return Task.FromResult(RunspaceManager.GetDiagnostics(code));
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());
        }
    }

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        ThrowIfDisposed();
        if (!_initialized) return Task.FromResult<HoverInfo?>(null);

        try
        {
            return Task.FromResult(RunspaceManager.GetHoverInfo(code, cursorPosition));
        }
        catch
        {
            return Task.FromResult<HoverInfo?>(null);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _initialized = false;

        _variableBridge = null;

        _runspaceManager?.Dispose();
        _runspaceManager = null;

        _executionLock.Dispose();

        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PowerShellKernel));
    }

    private void EnsureInitialized()
    {
        if (!_initialized) throw new InvalidOperationException("Kernel has not been initialized. Call InitializeAsync first.");
    }
}
