namespace Verso.Abstractions;

/// <summary>
/// Describes the layout position and size of a cell container within the notebook canvas.
/// </summary>
/// <param name="CellId">The unique identifier of the cell.</param>
/// <param name="X">The horizontal offset of the cell container, in device-independent pixels.</param>
/// <param name="Y">The vertical offset of the cell container, in device-independent pixels.</param>
/// <param name="Width">The width of the cell container, in device-independent pixels.</param>
/// <param name="Height">The height of the cell container, in device-independent pixels.</param>
/// <param name="IsVisible">Indicates whether the cell container is visible in the viewport. Defaults to <see langword="true"/>.</param>
public sealed record CellContainerInfo(
    Guid CellId,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsVisible = true);
