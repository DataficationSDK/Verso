namespace Verso.Abstractions;

/// <summary>
/// Specifies the resolved visibility of a cell within a specific layout.
/// </summary>
public enum CellVisibilityState
{
    /// <summary>
    /// Show the full cell including input and output.
    /// </summary>
    Visible,

    /// <summary>
    /// Hide the cell entirely.
    /// </summary>
    Hidden,

    /// <summary>
    /// Show only the cell's output area.
    /// </summary>
    OutputOnly,

    /// <summary>
    /// Show the cell in a collapsed summary state.
    /// </summary>
    Collapsed
}
