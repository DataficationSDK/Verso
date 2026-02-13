namespace Verso.Abstractions;

/// <summary>
/// Context provided to cell renderers when rendering a cell's visual output. Extends <see cref="IVersoContext"/> with cell layout and state information.
/// </summary>
public interface ICellRenderContext : IVersoContext
{
    /// <summary>
    /// Gets the unique identifier of the cell being rendered.
    /// </summary>
    Guid CellId { get; }

    /// <summary>
    /// Gets the read-only metadata dictionary attached to the cell.
    /// </summary>
    IReadOnlyDictionary<string, object> CellMetadata { get; }

    /// <summary>
    /// Gets the available rendering dimensions (width and height) in device-independent units.
    /// </summary>
    (double Width, double Height) Dimensions { get; }

    /// <summary>
    /// Gets a value indicating whether the cell is currently selected in the notebook UI.
    /// </summary>
    bool IsSelected { get; }
}
