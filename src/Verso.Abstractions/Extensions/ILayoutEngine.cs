namespace Verso.Abstractions;

/// <summary>
/// Manages the spatial arrangement of cells within a notebook. Layout engines support
/// different paradigms such as linear (top-to-bottom), grid, or freeform canvas layouts.
/// </summary>
public interface ILayoutEngine : IExtension
{
    /// <summary>
    /// Unique identifier for this layout engine (e.g. "linear", "grid", "freeform").
    /// </summary>
    string LayoutId { get; }

    /// <summary>
    /// Human-readable name shown in the layout picker.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Optional icon name or path representing this layout style.
    /// </summary>
    string? Icon { get; }

    /// <summary>
    /// Declares the capabilities of this layout engine (e.g. drag-and-drop, resizing, nesting).
    /// </summary>
    LayoutCapabilities Capabilities { get; }

    /// <summary>
    /// Renders the full layout for the given cells.
    /// </summary>
    /// <param name="cells">The ordered list of cell models to arrange.</param>
    /// <param name="context">Verso context providing theme, dimensions, and services.</param>
    /// <returns>A render result containing the complete layout output.</returns>
    Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context);

    /// <summary>
    /// Returns container information for a specific cell, describing its position and bounds within the layout.
    /// </summary>
    /// <param name="cellId">The unique identifier of the cell.</param>
    /// <param name="context">Verso context providing theme, dimensions, and services.</param>
    /// <returns>Container information describing the cell's placement in the layout.</returns>
    Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context);

    /// <summary>
    /// Notifies the layout engine that a new cell has been added at the specified index.
    /// </summary>
    /// <param name="cellId">The unique identifier of the added cell.</param>
    /// <param name="index">The zero-based index where the cell was inserted.</param>
    /// <param name="context">Verso context providing theme, dimensions, and services.</param>
    /// <returns>A task that completes when the layout has been updated.</returns>
    Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context);

    /// <summary>
    /// Notifies the layout engine that a cell has been removed.
    /// </summary>
    /// <param name="cellId">The unique identifier of the removed cell.</param>
    /// <param name="context">Verso context providing theme, dimensions, and services.</param>
    /// <returns>A task that completes when the layout has been updated.</returns>
    Task OnCellRemovedAsync(Guid cellId, IVersoContext context);

    /// <summary>
    /// Notifies the layout engine that a cell has been moved to a new index.
    /// </summary>
    /// <param name="cellId">The unique identifier of the moved cell.</param>
    /// <param name="newIndex">The zero-based destination index.</param>
    /// <param name="context">Verso context providing theme, dimensions, and services.</param>
    /// <returns>A task that completes when the layout has been updated.</returns>
    Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context);

    /// <summary>
    /// Returns layout-specific metadata (e.g. grid dimensions, cell positions) for persistence.
    /// </summary>
    /// <returns>A dictionary of metadata key-value pairs describing the current layout state.</returns>
    Dictionary<string, object> GetLayoutMetadata();

    /// <summary>
    /// Restores layout state from previously persisted metadata.
    /// </summary>
    /// <param name="metadata">The metadata dictionary previously returned by <see cref="GetLayoutMetadata"/>.</param>
    /// <param name="context">Verso context providing theme, dimensions, and services.</param>
    /// <returns>A task that completes when the layout state has been restored.</returns>
    Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context);
}
