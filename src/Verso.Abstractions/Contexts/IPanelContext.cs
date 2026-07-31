namespace Verso.Abstractions;

/// <summary>
/// Context provided to <see cref="INotebookPanel"/> when the host asks whether a
/// panel is available or asks it to produce content. Extends
/// <see cref="IVersoContext"/> with the notebook state a panel is most likely to
/// need.
/// </summary>
public interface IPanelContext : IVersoContext
{
    /// <summary>
    /// Gets the identifier of the currently selected cell, or <c>null</c> when no
    /// cell is selected. Panels that report on the selection should re-render when
    /// this changes; the host calls <see cref="INotebookPanel.RenderAsync"/> again
    /// on selection change.
    /// </summary>
    Guid? SelectedCellId { get; }

    /// <summary>
    /// Gets the ordered list of all cell models in the notebook.
    /// </summary>
    IReadOnlyList<CellModel> NotebookCells { get; }
}
