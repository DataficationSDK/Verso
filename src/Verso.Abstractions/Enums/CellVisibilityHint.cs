namespace Verso.Abstractions;

/// <summary>
/// Declares a cell type's default visibility nature as a hint to layout engines.
/// </summary>
public enum CellVisibilityHint
{
    /// <summary>
    /// The cell is presentable content. Layouts that filter cells should include this by default.
    /// </summary>
    Content,

    /// <summary>
    /// The cell is authoring infrastructure. Layouts that filter cells may exclude this by default.
    /// </summary>
    Infrastructure,

    /// <summary>
    /// The cell's output is presentable but its input is not. Layouts may show only the output area.
    /// </summary>
    OutputOnly
}
