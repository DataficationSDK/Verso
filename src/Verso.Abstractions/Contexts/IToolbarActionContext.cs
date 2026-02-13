namespace Verso.Abstractions;

/// <summary>
/// Context provided to toolbar actions, exposing the current cell selection and notebook state. Extends <see cref="IVersoContext"/>.
/// </summary>
public interface IToolbarActionContext : IVersoContext
{
    /// <summary>
    /// Gets the identifiers of the currently selected cells in the notebook UI.
    /// </summary>
    IReadOnlyList<Guid> SelectedCellIds { get; }

    /// <summary>
    /// Gets the ordered list of all cell models in the notebook.
    /// </summary>
    IReadOnlyList<CellModel> NotebookCells { get; }

    /// <summary>
    /// Gets the identifier of the currently active language kernel, or <c>null</c> if none is active.
    /// </summary>
    string? ActiveKernelId { get; }
}
