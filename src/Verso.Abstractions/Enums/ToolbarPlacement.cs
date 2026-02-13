namespace Verso.Abstractions;

/// <summary>
/// Specifies the location where a toolbar action or command is rendered.
/// </summary>
public enum ToolbarPlacement
{
    /// <summary>
    /// The primary toolbar displayed at the top of the notebook.
    /// </summary>
    MainToolbar,

    /// <summary>
    /// The inline toolbar displayed within an individual cell.
    /// </summary>
    CellToolbar,

    /// <summary>
    /// A right-click context menu associated with a cell or selection.
    /// </summary>
    ContextMenu
}
