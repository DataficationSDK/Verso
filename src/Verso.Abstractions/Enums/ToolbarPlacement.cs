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
    ContextMenu,

    /// <summary>
    /// The Export dropdown menu in the main toolbar. Actions placed here appear
    /// as items in the Export button's dropdown rather than as standalone buttons.
    /// </summary>
    ExportMenu
}
