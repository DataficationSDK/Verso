namespace Verso.Abstractions;

/// <summary>
/// Flags that describe which layout and editing operations a notebook layout supports.
/// </summary>
[Flags]
public enum LayoutCapabilities
{
    /// <summary>
    /// No layout capabilities are supported.
    /// </summary>
    None = 0,

    /// <summary>
    /// New cells can be inserted into the notebook.
    /// </summary>
    CellInsert = 1,

    /// <summary>
    /// Existing cells can be deleted from the notebook.
    /// </summary>
    CellDelete = 2,

    /// <summary>
    /// Cells can be reordered by dragging or moving.
    /// </summary>
    CellReorder = 4,

    /// <summary>
    /// Cell content can be edited by the user.
    /// </summary>
    CellEdit = 8,

    /// <summary>
    /// Cells can be resized within the layout.
    /// </summary>
    CellResize = 16,

    /// <summary>
    /// Cells can be executed to produce output.
    /// </summary>
    CellExecute = 32,

    /// <summary>
    /// Multiple cells can be selected at the same time.
    /// </summary>
    MultiSelect = 64
}
