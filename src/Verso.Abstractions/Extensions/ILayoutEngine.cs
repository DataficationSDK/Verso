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
    /// Indicates whether this layout requires a custom renderer (e.g. a webview panel)
    /// instead of the standard notebook cell-by-cell rendering.
    /// </summary>
    bool RequiresCustomRenderer { get; }

    /// <summary>
    /// Indicates how the layout's renderer relates to the host's UI execution context.
    /// Defaults to <see cref="LayoutRendererIsolation.Inline"/>. Layouts that need a private
    /// execution context should return <see cref="LayoutRendererIsolation.Isolated"/> and
    /// ship a renderer module via <see cref="GetRendererPackageAsync"/>.
    /// </summary>
    LayoutRendererIsolation RendererIsolation => LayoutRendererIsolation.Inline;

    /// <summary>
    /// Returns the package required to instantiate the layout's renderer in the client.
    /// Only consulted when <see cref="RendererIsolation"/> is
    /// <see cref="LayoutRendererIsolation.Isolated"/>; returning <c>null</c> signals that no
    /// package is available, in which case the host falls back to its default rendering path.
    /// </summary>
    Task<LayoutRendererPackage?> GetRendererPackageAsync(IVersoContext context)
        => Task.FromResult<LayoutRendererPackage?>(null);

    /// <summary>
    /// Returns the layout-scoped static assets (CSS today; future content types as the
    /// host adds them) that the host should load whenever this layout is active. The
    /// engine receives the host's <see cref="LayoutHostCapabilities"/> and is expected to
    /// return only assets whose content type the host advertised; the host filters
    /// defensively on receipt.
    /// </summary>
    /// <param name="context">Verso context providing theme, dimensions, and services.</param>
    /// <param name="hostCapabilities">Content types the host can consume.</param>
    /// <returns>
    /// The assets to load alongside the layout, or <c>null</c> when the engine has none.
    /// An empty list is treated the same as <c>null</c>.
    /// </returns>
    /// <remarks>
    /// Targets layouts with <see cref="RendererIsolation"/> equal to
    /// <see cref="LayoutRendererIsolation.Inline"/>. Isolated layouts continue to carry
    /// their assets through <see cref="LayoutRendererPackage.Files"/>.
    /// </remarks>
    Task<IReadOnlyList<LayoutStaticAsset>?> GetStaticAssetsAsync(
        IVersoContext context,
        LayoutHostCapabilities hostCapabilities)
        => Task.FromResult<IReadOnlyList<LayoutStaticAsset>?>(null);

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

    /// <summary>
    /// Declares the set of <see cref="CellVisibilityState"/> values this layout handles.
    /// The properties panel uses this to present only the states the layout supports.
    /// Defaults to <c>{ Visible }</c> (no filtering).
    /// </summary>
    IReadOnlySet<CellVisibilityState> SupportedVisibilityStates
        => new HashSet<CellVisibilityState> { CellVisibilityState.Visible };

    /// <summary>
    /// Indicates whether this layout supports the cell properties panel in the sidebar.
    /// Defaults to <c>false</c>.
    /// </summary>
    bool SupportsPropertiesPanel => false;
}
