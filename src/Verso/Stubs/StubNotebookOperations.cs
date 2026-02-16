using Verso.Abstractions;

namespace Verso.Stubs;

/// <summary>
/// No-op implementation of <see cref="INotebookOperations"/> for use when no Scaffold is available.
/// </summary>
public sealed class StubNotebookOperations : INotebookOperations
{
    public Task ExecuteCellAsync(Guid cellId) => Task.CompletedTask;
    public Task ExecuteAllAsync() => Task.CompletedTask;
    public Task ExecuteFromAsync(Guid cellId) => Task.CompletedTask;
    public Task ClearOutputAsync(Guid cellId) => Task.CompletedTask;
    public Task ClearAllOutputsAsync() => Task.CompletedTask;
    public Task RestartKernelAsync(string? kernelId = null) => Task.CompletedTask;
    public Task<string> InsertCellAsync(int index, string type, string? language = null)
        => Task.FromResult(Guid.NewGuid().ToString());
    public Task RemoveCellAsync(Guid cellId) => Task.CompletedTask;
    public Task MoveCellAsync(Guid cellId, int newIndex) => Task.CompletedTask;
    public Task ExecuteCodeAsync(string code, string? language = null, CancellationToken ct = default)
        => Task.CompletedTask;
    public string? ActiveLayoutId => null;
    public void SetActiveLayout(string layoutId) { }
    public string? ActiveThemeId { get; set; }
    public void SetActiveTheme(string themeId) { ActiveThemeId = themeId; }
}
