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
        Func<string, IExecutionContext, Task<IReadOnlyList<CellOutput>>>? executeFunc = null)
    {
        LanguageId = languageId;
        DisplayName = displayName;
        _executeFunc = executeFunc;
    }

    public string ExtensionId => $"com.test.{LanguageId}";
    public string Name => DisplayName;
    public string Version => "1.0.0";
    public string? Author => "Test";
    public string? Description => "Fake kernel for testing";
    public string LanguageId { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> FileExtensions => Array.Empty<string>();

    public int InitializeCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task InitializeAsync()
    {
        InitializeCallCount++;
        return Task.CompletedTask;
    }

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
