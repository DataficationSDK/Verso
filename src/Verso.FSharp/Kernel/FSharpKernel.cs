using Verso.Abstractions;
using Verso.FSharp.Helpers;

namespace Verso.FSharp.Kernel;

/// <summary>
/// F# Interactive language kernel for Verso notebooks.
/// Powered by FSharp.Compiler.Service (<c>FsiEvaluationSession</c>).
/// </summary>
[VersoExtension]
public sealed class FSharpKernel : ILanguageKernel
{
    private readonly FSharpKernelOptions _options;
    private SemaphoreSlim _executionLock = new(1, 1);
    private FsiSessionManager? _sessionManager;
    private VariableBridge? _variableBridge;
    private bool _variablesInjected;
    private bool _initialized;
    private bool _disposed;

    public FSharpKernel() : this(new FSharpKernelOptions()) { }

    internal FSharpKernel(FSharpKernelOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // --- IExtension ---

    public string ExtensionId => "verso.fsharp.kernel";
    public string Name => "F# (Interactive)";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "F# language kernel powered by FSharp.Compiler.Service.";

    // --- ILanguageKernel ---

    public string LanguageId => "fsharp";
    public string DisplayName => "F# (Interactive)";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".fs", ".fsx" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;

        // Support re-initialization after disposal (kernel restart)
        _disposed = false;

        _sessionManager = new FsiSessionManager();
        _sessionManager.Initialize(_options);

        // Evaluate default open declarations silently
        var opens = _options.DefaultOpens ?? FSharpKernelOptions.DefaultOpenNamespaces;
        foreach (var ns in opens)
        {
            _sessionManager.EvalSilent($"open {ns}");
        }

        // Add Verso.Abstractions reference so IVariableStore API is available in F# cells
        var abstractionsAssembly = typeof(Verso.Abstractions.IVariableStore).Assembly.Location;
        if (!string.IsNullOrEmpty(abstractionsAssembly))
        {
            _sessionManager.EvalSilent($"#r \"{abstractionsAssembly}\"");
        }

        _variableBridge = new VariableBridge(_options);
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

        await _executionLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            // Inject variables on first execution
            if (!_variablesInjected)
            {
                _variableBridge!.InjectVariables(_sessionManager!, context.Variables);
                _variablesInjected = true;
            }

            var result = _sessionManager!.EvalInteraction(code, context.CancellationToken);

            var outputs = new List<CellOutput>();

            // 1. FSI output (val bindings, type annotations, etc.)
            if (!string.IsNullOrEmpty(result.FsiOutput))
            {
                var fsiCell = new CellOutput("text/plain", result.FsiOutput);
                await context.WriteOutputAsync(fsiCell).ConfigureAwait(false);
                outputs.Add(fsiCell);
            }

            // 2. Console.Out capture
            if (!string.IsNullOrEmpty(result.ConsoleOutput))
            {
                var consoleCell = new CellOutput("text/plain", result.ConsoleOutput);
                await context.WriteOutputAsync(consoleCell).ConfigureAwait(false);
                outputs.Add(consoleCell);
            }

            // 3. Console.Error capture (as error output)
            if (!string.IsNullOrEmpty(result.ConsoleError))
            {
                var errCell = new CellOutput("text/plain", result.ConsoleError, IsError: true, ErrorName: "stderr");
                await context.WriteOutputAsync(errCell).ConfigureAwait(false);
                outputs.Add(errCell);
            }

            // 4. Compilation errors
            if (result.HasCompilationErrors)
            {
                var errorOutput = new CellOutput(
                    "text/plain",
                    result.CompilationErrorText ?? "Compilation error",
                    IsError: true,
                    ErrorName: "CompilationError");
                outputs.Add(errorOutput);
                return outputs;
            }

            // 5. Runtime exception (Choice2Of2)
            if (result.ResultValue is Exception ex)
            {
                var errorOutput = FormatException(ex);
                outputs.Add(errorOutput);
                return outputs;
            }

            // 6. Result value (if any, and not unit)
            if (result.ResultValue is not null)
            {
                // Attempt to resolve async values
                var resolved = await FSharpValueFormatter.ResolveAsyncValue(
                    result.ResultValue, context.CancellationToken).ConfigureAwait(false);

                if (resolved is not null)
                {
                    var formatted = FSharpValueFormatter.FormatValue(resolved);
                    if (!string.IsNullOrEmpty(formatted))
                    {
                        var valueCell = new CellOutput("text/plain", formatted);
                        await context.WriteOutputAsync(valueCell).ConfigureAwait(false);
                        outputs.Add(valueCell);
                    }
                }
            }

            // 7. Publish variables to the shared store
            _variableBridge!.PublishVariables(_sessionManager!, context.Variables);

            return outputs;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new[] { FormatException(ex) };
        }
        finally
        {
            _executionLock.Release();
        }
    }

    // --- IntelliSense stubs (Phase 2) ---

    public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        ThrowIfDisposed();
        return Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());
    }

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        ThrowIfDisposed();
        return Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());
    }

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        ThrowIfDisposed();
        return Task.FromResult<HoverInfo?>(null);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _initialized = false;

        _sessionManager?.Dispose();
        _sessionManager = null;
        _variableBridge?.Reset();
        _variableBridge = null;
        _variablesInjected = false;
        _executionLock.Dispose();

        return ValueTask.CompletedTask;
    }

    private static CellOutput FormatException(Exception ex)
    {
        // StackOverflow / OutOfMemory: simplified message suggesting kernel restart
        if (ex is StackOverflowException)
        {
            return new CellOutput(
                "text/plain",
                "Stack overflow. The computation exceeded the stack size limit. Consider restarting the kernel.",
                IsError: true,
                ErrorName: "StackOverflowException");
        }

        if (ex is OutOfMemoryException)
        {
            return new CellOutput(
                "text/plain",
                "Out of memory. The computation exceeded available memory. Consider restarting the kernel.",
                IsError: true,
                ErrorName: "OutOfMemoryException");
        }

        // MatchFailureException: include the unmatched value
        var exTypeName = ex.GetType().Name;
        if (exTypeName == "MatchFailureException")
        {
            return new CellOutput(
                "text/plain",
                $"MatchFailureException: {ex.Message}",
                IsError: true,
                ErrorName: "MatchFailureException",
                ErrorStackTrace: ex.StackTrace);
        }

        // General exception formatting with inner exception chain
        var message = $"{ex.GetType().FullName}: {ex.Message}";
        var inner = ex.InnerException;
        while (inner is not null)
        {
            message += $"{Environment.NewLine}  ---> {inner.GetType().FullName}: {inner.Message}";
            inner = inner.InnerException;
        }

        return new CellOutput(
            "text/plain",
            message,
            IsError: true,
            ErrorName: ex.GetType().Name,
            ErrorStackTrace: ex.StackTrace);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel has not been initialized. Call InitializeAsync first.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FSharpKernel));
    }
}
