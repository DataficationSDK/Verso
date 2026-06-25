using Microsoft.AspNetCore.Components;

namespace Verso.Blazor.Shared.Components.Notebook;

/// <summary>
/// Bundle of cell-list state and event callbacks that <c>NotebookPage</c> cascades to its
/// layout-renderer subtree. Keeps <c>LayoutRenderer</c>, <c>DefaultCellList</c>, and the
/// custom-layout cell pool from each having to thread a dozen-plus parameters through.
/// </summary>
public sealed class NotebookCellContext
{
    public Guid? SelectedCellId { get; init; }
    public Guid? ExecutingCellId { get; init; }

    /// <summary>
    /// True while any cell is executing. Server-side hosting flips this synchronously
    /// inside <c>StartExecutionAsync</c>; WASM hosting derives it from <c>ExecutingCellId</c>.
    /// The default cell list passes this directly to <c>Cell.IsRunDisabled</c>.
    /// </summary>
    public bool IsRunDisabled { get; init; }

    public IReadOnlySet<Guid> CollapsedSections { get; init; } = new HashSet<Guid>();

    public EventCallback<Guid> OnRunCell { get; init; }
    public EventCallback<Guid> OnCancelCell { get; init; }
    public EventCallback<Guid> OnDeleteCell { get; init; }
    public EventCallback<Guid> OnSelectCell { get; init; }
    public EventCallback<Guid> OnMoveUp { get; init; }
    public EventCallback<Guid> OnMoveDown { get; init; }
    public EventCallback<Guid> OnToggleSection { get; init; }
    public EventCallback<(Guid CellId, string Source)> OnSourceChanged { get; init; }
    public EventCallback<(Guid CellId, string Action)> OnCellAction { get; init; }
    public EventCallback<(Guid CellId, string NewType)> OnCellTypeChanged { get; init; }
    public EventCallback<(Guid CellId, string NewLanguage)> OnCellLanguageChanged { get; init; }
    public EventCallback<(int Index, string Type)> OnInsertCell { get; init; }
    public EventCallback<string> OnAddCell { get; init; }
    public EventCallback OnCellListClick { get; init; }
}
