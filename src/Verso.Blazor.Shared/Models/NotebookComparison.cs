using Verso.Abstractions;
using Verso.Blazor.Shared.Services;

namespace Verso.Blazor.Shared.Models;

/// <summary>
/// The live state of "this notebook is being measured against something": the baselines
/// on offer, the one in use, and the result.
/// </summary>
/// <remarks>
/// Kept out of the panel that draws it so a comparison survives the panel being closed,
/// and kept out of the page so the logic can be tested without rendering anything. The
/// page owns one of these and performs the transitions; the panel renders it and raises
/// intents.
/// </remarks>
public sealed class NotebookComparison
{
    private readonly INotebookService _service;
    private IReadOnlyDictionary<Guid, CellDiffKind> _cellMarks = EmptyMarks;

    private static readonly IReadOnlyDictionary<Guid, CellDiffKind> EmptyMarks
        = new Dictionary<Guid, CellDiffKind>();

    public NotebookComparison(INotebookService service) => _service = service;

    /// <summary>The baselines this notebook can be compared against, or <c>null</c> before they are loaded.</summary>
    public IReadOnlyList<DiffSourceInfo>? Sources { get; private set; }

    /// <summary>The computed diff, or <c>null</c> when nothing is being compared.</summary>
    public NotebookDiffResult? Result { get; private set; }

    /// <summary>The id of the source <see cref="Result"/> was computed against.</summary>
    public string? BaselineSourceId { get; private set; }

    /// <summary>True while sources are loading or a comparison is running.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>True when a comparison is in effect, whether or not the panel is open.</summary>
    public bool IsActive => Result is not null;

    /// <summary>
    /// The diff kind of every cell that still exists in the notebook, for decorating cells
    /// in place. Removed cells are absent because there is nothing left to decorate.
    /// </summary>
    public IReadOnlyDictionary<Guid, CellDiffKind> CellMarks => _cellMarks;

    /// <summary>
    /// Loads the available baselines. Safe to call repeatedly; the list can change as the
    /// notebook is saved or moved into a repository.
    /// </summary>
    /// <exception cref="Exception">Propagates the service failure so the caller can report it.</exception>
    public async Task LoadSourcesAsync()
    {
        IsBusy = true;
        try
        {
            Sources = await _service.GetDiffSourcesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Runs the comparison and keeps the result. A source that resolves to nothing (the user
    /// cancelled a native picker) leaves the current state alone rather than clearing it.
    /// </summary>
    /// <exception cref="Exception">Propagates the service failure so the caller can report it.</exception>
    public async Task<bool> CompareAsync(string sourceId, string? explicitInput = null)
    {
        IsBusy = true;
        try
        {
            var result = await _service.ComputeDiffAsync(sourceId, explicitInput);
            if (result is null)
                return false;

            Result = result;
            BaselineSourceId = sourceId;
            _cellMarks = BuildCellMarks(result);
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Drops the result and the cell marks, leaving the source list in place. Called when the
    /// user stops comparing and when a kernel restart makes the result stale.
    /// </summary>
    public void Clear()
    {
        Result = null;
        BaselineSourceId = null;
        _cellMarks = EmptyMarks;
    }

    private static IReadOnlyDictionary<Guid, CellDiffKind> BuildCellMarks(NotebookDiffResult result)
    {
        var marks = new Dictionary<Guid, CellDiffKind>();
        foreach (var entry in result.Cells)
        {
            // Removed cells have no current side, and unchanged ones are the baseline the
            // reader is comparing against rather than something to point at.
            if (entry.Kind == CellDiffKind.Unchanged || entry.CurrentCell is null)
                continue;

            marks[entry.CurrentCell.Id] = entry.Kind;
        }

        return marks;
    }
}
