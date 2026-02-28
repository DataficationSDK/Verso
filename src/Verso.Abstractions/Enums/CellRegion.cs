namespace Verso.Abstractions;

/// <summary>
/// Specifies which region of a cell originated an interaction.
/// </summary>
public enum CellRegion
{
    /// <summary>
    /// The input (source code / markup) region of the cell.
    /// </summary>
    Input,

    /// <summary>
    /// The output (rendered result) region of the cell.
    /// </summary>
    Output
}
