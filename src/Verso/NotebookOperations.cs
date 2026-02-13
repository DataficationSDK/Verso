using Verso.Abstractions;

namespace Verso;

/// <summary>
/// Engine-side implementation of <see cref="INotebookOperations"/>, wired into the <see cref="Scaffold"/>.
/// </summary>
internal sealed class NotebookOperations : INotebookOperations
{
    private readonly Scaffold _scaffold;

    public NotebookOperations(Scaffold scaffold)
    {
        _scaffold = scaffold ?? throw new ArgumentNullException(nameof(scaffold));
    }

    public async Task ExecuteCellAsync(Guid cellId)
    {
        await _scaffold.ExecuteCellAsync(cellId).ConfigureAwait(false);
    }

    public async Task ExecuteAllAsync()
    {
        await _scaffold.ExecuteAllAsync().ConfigureAwait(false);
    }

    public async Task ExecuteFromAsync(Guid cellId)
    {
        var cells = _scaffold.Cells;
        var startIndex = -1;
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].Id == cellId)
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0)
            throw new InvalidOperationException($"Cell {cellId} not found.");

        for (int i = startIndex; i < cells.Count; i++)
        {
            await _scaffold.ExecuteCellAsync(cells[i].Id).ConfigureAwait(false);
        }
    }

    public Task ClearOutputAsync(Guid cellId)
    {
        var cell = _scaffold.GetCell(cellId)
            ?? throw new InvalidOperationException($"Cell {cellId} not found.");
        cell.Outputs.Clear();
        return Task.CompletedTask;
    }

    public Task ClearAllOutputsAsync()
    {
        _scaffold.ClearAllOutputs();
        return Task.CompletedTask;
    }

    public async Task RestartKernelAsync(string? kernelId = null)
    {
        await _scaffold.RestartKernelAsync(kernelId).ConfigureAwait(false);
    }

    public Task<string> InsertCellAsync(int index, string type, string? language = null)
    {
        var capabilities = _scaffold.LayoutCapabilities;
        if (!capabilities.HasFlag(LayoutCapabilities.CellInsert))
            throw new LayoutCapabilityException(LayoutCapabilities.CellInsert);

        var cell = _scaffold.InsertCell(index, type, language);
        return Task.FromResult(cell.Id.ToString());
    }

    public Task RemoveCellAsync(Guid cellId)
    {
        var capabilities = _scaffold.LayoutCapabilities;
        if (!capabilities.HasFlag(LayoutCapabilities.CellDelete))
            throw new LayoutCapabilityException(LayoutCapabilities.CellDelete);

        if (!_scaffold.RemoveCell(cellId))
            throw new InvalidOperationException($"Cell {cellId} not found.");

        return Task.CompletedTask;
    }

    public Task MoveCellAsync(Guid cellId, int newIndex)
    {
        var capabilities = _scaffold.LayoutCapabilities;
        if (!capabilities.HasFlag(LayoutCapabilities.CellReorder))
            throw new LayoutCapabilityException(LayoutCapabilities.CellReorder);

        var cells = _scaffold.Cells;
        var fromIndex = -1;
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].Id == cellId)
            {
                fromIndex = i;
                break;
            }
        }

        if (fromIndex < 0)
            throw new InvalidOperationException($"Cell {cellId} not found.");

        _scaffold.MoveCell(fromIndex, newIndex);
        return Task.CompletedTask;
    }
}
