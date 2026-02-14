using Verso.Abstractions;

namespace Verso.Tests.Helpers;

/// <summary>
/// Test double for <see cref="INotebookOperations"/> that tracks all operation calls for assertion.
/// </summary>
public sealed class StubNotebookOperations : INotebookOperations
{
    public List<Guid> ExecutedCellIds { get; } = new();
    public int ExecuteAllCallCount { get; private set; }
    public List<Guid> ExecuteFromCellIds { get; } = new();
    public List<Guid> ClearedOutputCellIds { get; } = new();
    public int ClearAllOutputsCallCount { get; private set; }
    public List<string?> RestartedKernelIds { get; } = new();
    public List<(int Index, string Type, string? Language)> InsertedCells { get; } = new();
    public List<Guid> RemovedCellIds { get; } = new();
    public List<(Guid CellId, int NewIndex)> MovedCells { get; } = new();

    public Task ExecuteCellAsync(Guid cellId)
    {
        ExecutedCellIds.Add(cellId);
        return Task.CompletedTask;
    }

    public Task ExecuteAllAsync()
    {
        ExecuteAllCallCount++;
        return Task.CompletedTask;
    }

    public Task ExecuteFromAsync(Guid cellId)
    {
        ExecuteFromCellIds.Add(cellId);
        return Task.CompletedTask;
    }

    public Task ClearOutputAsync(Guid cellId)
    {
        ClearedOutputCellIds.Add(cellId);
        return Task.CompletedTask;
    }

    public Task ClearAllOutputsAsync()
    {
        ClearAllOutputsCallCount++;
        return Task.CompletedTask;
    }

    public Task RestartKernelAsync(string? kernelId = null)
    {
        RestartedKernelIds.Add(kernelId);
        return Task.CompletedTask;
    }

    public Task<string> InsertCellAsync(int index, string type, string? language = null)
    {
        InsertedCells.Add((index, type, language));
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task RemoveCellAsync(Guid cellId)
    {
        RemovedCellIds.Add(cellId);
        return Task.CompletedTask;
    }

    public Task MoveCellAsync(Guid cellId, int newIndex)
    {
        MovedCells.Add((cellId, newIndex));
        return Task.CompletedTask;
    }

    public string? ActiveLayoutId { get; set; }

    public void SetActiveLayout(string layoutId)
    {
        ActiveLayoutId = layoutId;
    }

    public string? ActiveThemeId { get; set; }
    public List<string> SwitchedThemeIds { get; } = new();

    public void SetActiveTheme(string themeId)
    {
        ActiveThemeId = themeId;
        SwitchedThemeIds.Add(themeId);
    }
}
