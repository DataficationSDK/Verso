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
    IReadOnlyList<string> RegisteredLanguages { get; }

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

    /// <summary>Raised when the notebook structure changes (add, remove, move, new, open).</summary>
    event Action? OnNotebookChanged;

    /// <summary>Raised when the active layout changes.</summary>
    event Action? OnLayoutChanged;

    /// <summary>Raised when the active theme changes.</summary>
    event Action? OnThemeChanged;

    /// <summary>Raised when an extension is enabled or disabled.</summary>
    event Action? OnExtensionStatusChanged;

    /// <summary>Raised when variables in the store change.</summary>
    event Action? OnVariablesChanged;

    /// <summary>Raised when an extension setting changes.</summary>
    event Action? OnSettingsChanged;

    // ── File operations ────────────────────────────────────────────────

    /// <summary>Create a new empty notebook.</summary>
    Task NewNotebookAsync();

    /// <summary>Open a notebook from a file path.</summary>
    Task OpenAsync(string filePath);

    /// <summary>Open a notebook from in-memory content.</summary>
    Task OpenFromContentAsync(string fileName, string content);

    /// <summary>Save the notebook to a file path.</summary>
    Task SaveAsync(string filePath);

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

    /// <summary>Clear all cell outputs.</summary>
    Task ClearAllOutputsAsync();

    // ── Execution ──────────────────────────────────────────────────────

    /// <summary>Execute a single cell.</summary>
    Task<ExecutionResultDto> ExecuteCellAsync(Guid cellId);

    /// <summary>Execute all cells in order.</summary>
    Task<IReadOnlyList<ExecutionResultDto>> ExecuteAllAsync();

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

    // ── Editor intelligence ────────────────────────────────────────────

    /// <summary>Get hover information for a position in a cell.</summary>
    Task<HoverResultDto?> GetHoverInfoAsync(Guid cellId, string code, int position);

    /// <summary>Get completions for a position in a cell.</summary>
    Task<CompletionsResultDto?> GetCompletionsAsync(Guid cellId, string code, int position);

    // ── Layout & theme switching ───────────────────────────────────────

    /// <summary>Switch the active layout.</summary>
    Task SwitchLayoutAsync(string layoutId);

    /// <summary>Switch the active theme.</summary>
    Task SwitchThemeAsync(string themeId);

    // ── Extension management ───────────────────────────────────────────

    /// <summary>Enable an extension by ID.</summary>
    Task EnableExtensionAsync(string extensionId);

    /// <summary>Disable an extension by ID.</summary>
    Task DisableExtensionAsync(string extensionId);

    // ── Settings ───────────────────────────────────────────────────────

    /// <summary>Get all setting definitions grouped by extension.</summary>
    IReadOnlyList<ExtensionSettingsGroup> GetSettingDefinitions();

    /// <summary>Get the current value for a setting.</summary>
    object? GetSettingValue(string extensionId, string settingName);

    /// <summary>Update a setting value.</summary>
    Task UpdateSettingAsync(string extensionId, string settingName, object? value);

    // ── Variables ──────────────────────────────────────────────────────

    /// <summary>Get all variables from the variable store.</summary>
    IReadOnlyList<VariableEntryDto> GetVariables();

    /// <summary>Inspect a variable by name (formatted output).</summary>
    Task<VariableInspectResultDto?> InspectVariableAsync(string name);

    // ── Dashboard layout ───────────────────────────────────────────────

    /// <summary>Get the grid position for a cell in the dashboard layout.</summary>
    Task<CellContainerInfo> GetCellContainerAsync(Guid cellId);

    /// <summary>Update cell position in dashboard layout.</summary>
    Task UpdateCellPositionAsync(Guid cellId, int row, int col, int colSpan, int rowSpan);

    // ── Cell type helpers ──────────────────────────────────────────────

    /// <summary>Whether the input should be collapsed for a given cell type after execution.</summary>
    bool ShouldCollapseInput(string cellType);
}
