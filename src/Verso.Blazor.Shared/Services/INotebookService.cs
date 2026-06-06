using Verso.Abstractions;
using Verso.Blazor.Shared.Models;

namespace Verso.Blazor.Shared.Services;

/// <summary>
/// Abstraction layer between Blazor UI components and the notebook engine.
/// Implemented in-process by <c>ServerNotebookService</c> (Blazor Server)
/// and remotely by <c>RemoteNotebookService</c> (Blazor WASM via JSON-RPC bridge).
/// </summary>
public interface INotebookService
{
    // ── State ──────────────────────────────────────────────────────────

    /// <summary>Whether a notebook is currently loaded.</summary>
    bool IsLoaded { get; }

    /// <summary>Whether this service is running in an embedded context (e.g. VS Code webview).</summary>
    bool IsEmbedded { get; }

    /// <summary>The on-disk file path, or null if unsaved / embedded.</summary>
    string? FilePath { get; }

    // ── Notebook metadata ──────────────────────────────────────────────

    /// <summary>Notebook title.</summary>
    string? Title { get; set; }

    /// <summary>Default kernel language for new code cells.</summary>
    string? DefaultKernelId { get; set; }

    /// <summary>Languages registered with the kernel system.</summary>
    IReadOnlyList<KernelLanguageInfo> RegisteredLanguages { get; }

    /// <summary>When the notebook was created.</summary>
    DateTimeOffset? Created { get; }

    /// <summary>When the notebook was last modified.</summary>
    DateTimeOffset? Modified { get; }

    /// <summary>Format version string.</summary>
    string FormatVersion { get; }

    // ── Cells ──────────────────────────────────────────────────────────

    /// <summary>The ordered list of cells in the notebook.</summary>
    IReadOnlyList<CellModel> Cells { get; }

    // ── Layout & theme ─────────────────────────────────────────────────

    /// <summary>Whether the active layout uses a custom renderer (e.g. dashboard grid).</summary>
    bool IsDashboardLayout { get; }

    /// <summary>Active theme kind (Light or Dark).</summary>
    ThemeKind? ActiveThemeKind { get; }

    /// <summary>Active theme data for CSS variable generation.</summary>
    ThemeData? ActiveThemeData { get; }

    /// <summary>Active layout ID.</summary>
    string? ActiveLayoutId { get; }

    /// <summary>Qualified (ExtensionId, LayoutId) identity of the active layout, or null.</summary>
    LayoutReference? ActiveLayout { get; }

    /// <summary>
    /// Stable composite key for the active layout in the form <c>{ExtensionId}:{LayoutId}</c>,
    /// or a sentinel value when no layout is loaded. Used by <c>NotebookPage</c> to <c>@key</c>
    /// the layout-renderer subtree so Blazor tears the prior layout's DOM down on switch.
    /// </summary>
    string ActiveLayoutKey { get; }

    /// <summary>
    /// Rendering isolation mode of the active layout when it requires a custom renderer.
    /// Returns null when there is no active layout or the layout does not require a custom renderer.
    /// </summary>
    string? ActiveLayoutRendererIsolation { get; }

    /// <summary>Capabilities granted by the active layout (cell insert, delete, execute, etc.).</summary>
    LayoutCapabilities LayoutCapabilities { get; }

    /// <summary>Whether the active layout supports the cell properties panel.</summary>
    bool ActiveLayoutSupportsPropertiesPanel { get; }

    /// <summary>Active theme ID.</summary>
    string? ActiveThemeId { get; }

    // ── Extension data ─────────────────────────────────────────────────

    /// <summary>Available cell types (code, markdown, extension-defined).</summary>
    IReadOnlyList<CellTypeInfo> AvailableCellTypes { get; }

    /// <summary>Available layout engines.</summary>
    IReadOnlyList<LayoutInfo> AvailableLayouts { get; }

    /// <summary>Available themes.</summary>
    IReadOnlyList<ThemeInfo> AvailableThemes { get; }

    /// <summary>Loaded extension information.</summary>
    IReadOnlyList<ExtensionInfo> Extensions { get; }

    // ── Events ─────────────────────────────────────────────────────────

    /// <summary>Raised after a cell finishes execution.</summary>
    event Action? OnCellExecuted;

    /// <summary>Raised when a cell is about to begin execution. Carries the cell ID.</summary>
    event Action<Guid>? OnCellExecuting;

    /// <summary>Raised after a cell finishes execution, with the cell ID.</summary>
    event Action<Guid>? OnCellExecutionCompleted;

    /// <summary>Raised when the notebook structure changes (add, remove, move, new, open).</summary>
    event Action? OnNotebookChanged;

    /// <summary>Raised when the active layout changes.</summary>
    event Action? OnLayoutChanged;

    /// <summary>
    /// Raised when the host emits a <c>layout/updated</c> notification in response to
    /// a layout interaction handler calling <c>RequestRender</c> or <c>RequestCellRefresh</c>.
    /// Subscribers should filter by <see cref="LayoutUpdatedEventArgs.ExtensionId"/> and
    /// <see cref="LayoutUpdatedEventArgs.LayoutId"/> matching the currently active layout.
    /// </summary>
    event Action<LayoutUpdatedEventArgs>? OnLayoutUpdated;

    /// <summary>Raised when the active theme changes.</summary>
    event Action? OnThemeChanged;

    /// <summary>Raised when an extension is enabled or disabled.</summary>
    event Action? OnExtensionStatusChanged;

    /// <summary>Raised when variables in the store change.</summary>
    event Action? OnVariablesChanged;

    /// <summary>Raised when an extension setting changes.</summary>
    event Action? OnSettingsChanged;

    /// <summary>Raised when a cell output is updated in place by an interaction handler.</summary>
    event Action? OnOutputUpdated;

    /// <summary>Raised when the kernel begins restarting. The kernel ID is provided where the
    /// host tracks it (WASM); it may be null on hosts that do not surface a per-kernel identity.</summary>
    event Action<string?>? OnKernelRestarting;

    /// <summary>Raised after the kernel has finished restarting and notebook state has been
    /// re-synchronized. The kernel ID is provided where the host tracks it (WASM); it may be null
    /// on hosts that do not surface a per-kernel identity.</summary>
    event Action<string?>? OnKernelRestarted;

    // ── File operations ────────────────────────────────────────────────

    /// <summary>Create a new empty notebook.</summary>
    Task NewNotebookAsync();

    /// <summary>Open a notebook from a file path.</summary>
    Task OpenAsync(string filePath);

    /// <summary>Open a notebook from in-memory content.</summary>
    Task OpenFromContentAsync(string fileName, string content);

    /// <summary>Save the notebook to a file path.</summary>
    Task SaveAsync(string filePath);

    /// <summary>Serialize the notebook content without writing to disk.</summary>
    Task<string?> GetSerializedContentAsync();

    // ── Cell operations ────────────────────────────────────────────────

    /// <summary>Add a new cell at the end.</summary>
    Task<CellModel> AddCellAsync(string type = "code", string? language = null);

    /// <summary>Insert a new cell at the specified index.</summary>
    Task<CellModel> InsertCellAsync(int index, string type = "code", string? language = null);

    /// <summary>Remove a cell by ID.</summary>
    Task<bool> RemoveCellAsync(Guid cellId);

    /// <summary>Move a cell from one position to another.</summary>
    Task MoveCellAsync(int fromIndex, int toIndex);

    /// <summary>Update the source of a cell.</summary>
    Task UpdateCellSourceAsync(Guid cellId, string source);

    /// <summary>Change the type of an existing cell.</summary>
    Task ChangeCellTypeAsync(Guid cellId, string newType);

    /// <summary>Change the kernel language of an existing code cell.</summary>
    Task ChangeCellLanguageAsync(Guid cellId, string newLanguage);

    /// <summary>Clear all cell outputs.</summary>
    Task ClearAllOutputsAsync();

    // ── Execution ──────────────────────────────────────────────────────

    /// <summary>Execute a single cell.</summary>
    Task<ExecutionResultDto> ExecuteCellAsync(Guid cellId);

    /// <summary>Execute all cells in order.</summary>
    Task<IReadOnlyList<ExecutionResultDto>> ExecuteAllAsync();

    /// <summary>Cancel the in-flight execution, if any. The cell ID is currently
    /// informational; cancellation targets whichever cell is currently running.</summary>
    Task CancelCellAsync(Guid cellId);

    /// <summary>Restart the active kernel.</summary>
    Task RestartKernelAsync();

    // ── Toolbar actions ────────────────────────────────────────────────

    /// <summary>Get toolbar actions for the specified placement.</summary>
    IReadOnlyList<ToolbarActionInfo> GetToolbarActions(ToolbarPlacement placement);

    /// <summary>Batch-check enabled states for actions at a placement.</summary>
    Task<Dictionary<string, bool>> GetActionEnabledStatesAsync(
        ToolbarPlacement placement, IReadOnlyList<Guid> selectedCellIds);

    /// <summary>Execute a toolbar action by ID.</summary>
    Task ExecuteActionAsync(string actionId, IReadOnlyList<Guid> selectedCellIds);

    // ── Cell interaction ──────────────────────────────────────────────

    /// <summary>Send an interaction event from rendered cell content to the extension that owns it.</summary>
    Task<string?> HandleCellInteractionAsync(Guid cellId, string extensionId, string interactionType,
        string payload, string? outputBlockId, CellRegion region);

    // ── Editor intelligence ────────────────────────────────────────────

    /// <summary>Get hover information for a position in a cell.</summary>
    Task<HoverResultDto?> GetHoverInfoAsync(Guid cellId, string code, int position);

    /// <summary>Get completions for a position in a cell.</summary>
    Task<CompletionsResultDto?> GetCompletionsAsync(Guid cellId, string code, int position);

    // ── Layout & theme switching ───────────────────────────────────────

    /// <summary>
    /// Renders the currently active layout, returning the layout-emitted HTML payload.
    /// Returns null when the active layout does not produce custom-renderer HTML in the
    /// current hosting mode.
    /// </summary>
    Task<string?> RenderActiveLayoutAsync();

    /// <summary>Switch the active layout.</summary>
    Task SwitchLayoutAsync(string layoutId);

    /// <summary>
    /// Returns the layout-scoped static assets for the given layout, each resolved to a URL
    /// the host can drop into a <c>&lt;link rel="stylesheet"&gt;</c> tag. Server hosts
    /// resolve to a static-files endpoint URL; WASM hosts resolve to a <c>blob:</c> URL
    /// produced via JS interop. Returns an empty list when the layout has no assets.
    /// </summary>
    Task<IReadOnlyList<LayoutStaticAssetDescriptor>> GetLayoutStaticAssetsAsync(
        string extensionId,
        string layoutId);

    /// <summary>
    /// Releases any per-session resources the host allocated for a given layout's assets
    /// (e.g. <c>blob:</c> URLs minted by the WASM host). Default is a no-op for hosts that
    /// resolve assets via plain server URLs.
    /// </summary>
    Task ReleaseLayoutStaticAssetsAsync(string extensionId, string layoutId)
        => Task.CompletedTask;

    /// <summary>Switch the active theme.</summary>
    Task SwitchThemeAsync(string themeId);

    // ── Extension management ───────────────────────────────────────────

    /// <summary>Enable an extension by ID.</summary>
    Task EnableExtensionAsync(string extensionId);

    /// <summary>Disable an extension by ID.</summary>
    Task DisableExtensionAsync(string extensionId);

    // ── Extension marketplace ──────────────────────────────────────────

    /// <summary>
    /// Whether this host supports searching for and installing extensions from NuGet.
    /// The extension panel hides its search box when this is false.
    /// </summary>
    bool IsMarketplaceSupported { get; }

    /// <summary>
    /// Searches NuGet for extension packages matching <paramref name="query"/>. Results
    /// carry an installed flag relative to the current notebook's required extensions.
    /// </summary>
    Task<IReadOnlyList<PackageSearchResultDto>> SearchExtensionsAsync(
        string query, int skip, int take, bool includePrerelease, CancellationToken ct);

    /// <summary>
    /// Downloads and installs a NuGet package into the managed extensions directory, loads
    /// its extensions, records it in the notebook's required extensions, and persists trust
    /// so future opens load it without prompting. Surfaces the existing consent dialog when
    /// the package is not yet trusted.
    /// </summary>
    Task<PackageInstallResultDto> InstallExtensionAsync(
        string packageId, string? version, CancellationToken ct);

    /// <summary>
    /// Removes a package from the notebook's required extensions and revokes its trust. The
    /// extension's capabilities remain active until the next open; the on-disk files are kept
    /// so it can be reinstalled.
    /// </summary>
    Task UninstallExtensionAsync(string packageId);

    // ── Settings ───────────────────────────────────────────────────────

    /// <summary>Get all setting definitions grouped by extension.</summary>
    IReadOnlyList<ExtensionSettingsGroup> GetSettingDefinitions();

    /// <summary>Get the current value for a setting.</summary>
    object? GetSettingValue(string extensionId, string settingName);

    /// <summary>Update a setting value.</summary>
    Task UpdateSettingAsync(string extensionId, string settingName, object? value);

    // ── Variables ──────────────────────────────────────────────────────

    /// <summary>Get all variables from the variable store (reads from cache in WASM).</summary>
    IReadOnlyList<VariableEntryDto> GetVariables();

    /// <summary>Force-refresh the variable list from the host/engine. In Server mode this re-reads
    /// the live store; in WASM mode this sends a variable/list request to the host.</summary>
    Task RefreshVariablesAsync();

    /// <summary>Inspect a variable by name (formatted output).</summary>
    Task<VariableInspectResultDto?> InspectVariableAsync(string name);

    // ── Cell properties ──────────────────────────────────────────────

    /// <summary>Get ordered property sections for the given cell from all applicable providers.</summary>
    Task<IReadOnlyList<PropertySectionResult>> GetCellPropertySectionsAsync(Guid cellId);

    /// <summary>Notify a property provider that a field value was changed in the panel.</summary>
    Task NotifyPropertyChangedAsync(Guid cellId, string providerExtensionId, string propertyName, object? value);

    /// <summary>Resolve the visibility state of a cell for the current active layout.</summary>
    CellVisibilityState ResolveCellVisibility(Guid cellId);

    // ── Layout interaction ─────────────────────────────────────────────

    /// <summary>
    /// Routes a layout-scoped interaction to the layout's
    /// <c>ILayoutInteractionHandler</c> via the <c>layout/interact</c> JSON-RPC
    /// method. First-party razor components call this directly; DOM events
    /// flow through the JS bridge instead.
    /// </summary>
    Task LayoutInteractAsync(
        string extensionId,
        string layoutId,
        string interactionType,
        string payload,
        string? frameInstanceId = null,
        string? targetId = null);

    // ── Dashboard layout ───────────────────────────────────────────────

    /// <summary>Get the grid position for a cell in the dashboard layout.</summary>
    Task<CellContainerInfo> GetCellContainerAsync(Guid cellId);

    /// <summary>Update cell position in dashboard layout.</summary>
    [Obsolete("Forwards to LayoutInteractAsync. Call LayoutInteractAsync(\"verso.layout.dashboard\", \"dashboard\", \"updateCellPosition\", ...) directly. Scheduled for removal in v2.0.")]
    Task UpdateCellPositionAsync(Guid cellId, int row, int col, int colSpan, int rowSpan);

    // ── Cell type helpers ──────────────────────────────────────────────

    /// <summary>Whether the input should be collapsed for a given cell type after execution.</summary>
    bool ShouldCollapseInput(string cellType);

    /// <summary>Whether the cell type uses a text editor. Returns <c>false</c> for form-based cell types like parameters.</summary>
    bool IsCellTypeEditable(string cellType);
}
