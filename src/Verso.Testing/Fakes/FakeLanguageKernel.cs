using Verso.Abstractions;

namespace Verso.Testing.Fakes;

/// <summary>
/// Configurable <see cref="ILanguageKernel"/> test double with injectable execution behavior.
/// </summary>
public sealed class FakeLanguageKernel : ILanguageKernel
{
    private readonly Func<string, IExecutionContext, Task<IReadOnlyList<CellOutput>>>? _executeFunc;

    public FakeLanguageKernel(
        string languageId = "fake",
        string displayName = "Fake",
        Func<string, IExecutionContext, Task<IReadOnlyList<CellOutput>>>? executeFunc = null,
        IReadOnlyList<string>? fileExtensions = null,
        TimeSpan? initializeDelay = null)
    {
        LanguageId = languageId;
        DisplayName = displayName;
        _executeFunc = executeFunc;
        FileExtensions = fileExtensions ?? Array.Empty<string>();
        InitializeDelay = initializeDelay ?? TimeSpan.Zero;
    }

    /// <summary>
    /// Artificial delay applied inside <see cref="InitializeAsync"/>. Used by tests
    /// that need to open a window where concurrent initializers can race.
    /// </summary>
    public TimeSpan InitializeDelay { get; }

    public string ExtensionId => $"com.test.{LanguageId}";
    public string Name => DisplayName;
    public string Version => "1.0.0";
    public string? Author => "Test";
    public string? Description => "Fake kernel for testing";
    public string LanguageId { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> FileExtensions { get; }

    public int InitializeCallCount => _initializeCallCount;
    public int DisposeCallCount { get; private set; }

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        Interlocked.Increment(ref _initializeCallCount);
        if (InitializeDelay > TimeSpan.Zero)
            await Task.Delay(InitializeDelay).ConfigureAwait(false);
    }

    private int _initializeCallCount;

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        if (_executeFunc is not null)
            return await _executeFunc(code, context).ConfigureAwait(false);

        return new[] { new CellOutput("text/plain", $"Executed: {code}") };
    }

    public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
        => Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
        => Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
        => Task.FromResult<HoverInfo?>(null);

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        return ValueTask.CompletedTask;
    }
}
