using Verso.Abstractions;
using Verso.Blazor.Shared.Models;
using Verso.Blazor.Shared.Services;

namespace Verso.Blazor.Shared.Tests.Fakes;

/// <summary>
/// Configurable fake implementation of <see cref="INotebookService"/> for bUnit tests.
/// All properties return sensible defaults that can be overridden per test.
/// </summary>
public sealed class FakeNotebookService : INotebookService
{
    // ── State ──────────────────────────────────────────────────────────

    public bool IsLoaded { get; set; } = true;
    public bool IsEmbedded { get; set; }
    public string? FilePath { get; set; } = "/fake/notebook.verso";

    // ── Notebook metadata ──────────────────────────────────────────────

    public string? Title { get; set; } = "Test Notebook";
    public string? DefaultKernelId { get; set; } = "csharp";
    public IReadOnlyList<string> RegisteredLanguages { get; set; } = new List<string> { "csharp", "fsharp" };
    public DateTimeOffset? Created { get; set; } = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset? Modified { get; set; } = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
    public string FormatVersion { get; set; } = "1.0";

    // ── Cells ──────────────────────────────────────────────────────────

    public IReadOnlyList<CellModel> Cells { get; set; } = new List<CellModel>();

    // ── Layout & theme ─────────────────────────────────────────────────

    public bool IsDashboardLayout { get; set; }
    public ThemeKind? ActiveThemeKind { get; set; } = ThemeKind.Light;
    public ThemeData? ActiveThemeData { get; set; }
    public string? ActiveLayoutId { get; set; } = "notebook";
    public string? ActiveThemeId { get; set; } = "light";

    // ── Extension data ─────────────────────────────────────────────────

    public IReadOnlyList<CellTypeInfo> AvailableCellTypes { get; set; } = new List<CellTypeInfo>
    {
        new("code", "Code"),
        new("markdown", "Markdown")
    };

    public IReadOnlyList<LayoutInfo> AvailableLayouts { get; set; } = new List<LayoutInfo>
    {
        new("notebook", "Notebook", false)
    };

    public IReadOnlyList<ThemeInfo> AvailableThemes { get; set; } = new List<ThemeInfo>
    {
        new("light", "Light", ThemeKind.Light)
    };

    public IReadOnlyList<ExtensionInfo> Extensions { get; set; } = new List<ExtensionInfo>();

    // ── Events ─────────────────────────────────────────────────────────

    public event Action? OnCellExecuted;
    public event Action? OnNotebookChanged;
    public event Action? OnLayoutChanged;
    public event Action? OnThemeChanged;
    public event Action? OnExtensionStatusChanged;
    public event Action? OnVariablesChanged;
    public event Action? OnSettingsChanged;

    // ── Call tracking ──────────────────────────────────────────────────

    public List<string> AddCellCalls { get; } = new();
    public List<Guid> ExecutedCellIds { get; } = new();
    public int ExecuteAllCallCount { get; private set; }
    public int RestartKernelCallCount { get; private set; }
    public int NewNotebookCallCount { get; private set; }
    public int SaveCallCount { get; private set; }
    public string? LastSavePath { get; private set; }
    public List<string> SwitchLayoutCalls { get; } = new();
    public List<string> SwitchThemeCalls { get; } = new();
    public List<string> EnableExtensionCalls { get; } = new();
    public List<string> DisableExtensionCalls { get; } = new();
    public List<(string ExtensionId, string SettingName, object? Value)> UpdateSettingCalls { get; } = new();

    // ── Configurable responses ─────────────────────────────────────────

    public List<ToolbarActionInfo> ToolbarActions { get; set; } = new();
    public Dictionary<string, bool> ActionEnabledStates { get; set; } = new();
    public List<ExtensionSettingsGroup> SettingDefinitions { get; set; } = new();
    public List<VariableEntryDto> Variables { get; set; } = new();
    public VariableInspectResultDto? InspectResult { get; set; }
    public Dictionary<Guid, CellContainerInfo> CellContainers { get; set; } = new();
    public Dictionary<string, bool> CollapseInputMap { get; set; } = new();

    // ── File operations ────────────────────────────────────────────────

    public Task NewNotebookAsync()
    {
        NewNotebookCallCount++;
        return Task.CompletedTask;
    }

    public Task OpenAsync(string filePath) => Task.CompletedTask;

    public Task OpenFromContentAsync(string fileName, string content) => Task.CompletedTask;

    public Task SaveAsync(string filePath)
    {
        SaveCallCount++;
        LastSavePath = filePath;
        return Task.CompletedTask;
    }

    // ── Cell operations ────────────────────────────────────────────────

    public Task<CellModel> AddCellAsync(string type = "code", string? language = null)
    {
        AddCellCalls.Add(type);
        var cell = new CellModel { Type = type, Language = language };
        return Task.FromResult(cell);
    }

    public Task<CellModel> InsertCellAsync(int index, string type = "code", string? language = null)
    {
        var cell = new CellModel { Type = type, Language = language };
        return Task.FromResult(cell);
    }

    public Task<bool> RemoveCellAsync(Guid cellId) => Task.FromResult(true);

    public Task MoveCellAsync(int fromIndex, int toIndex) => Task.CompletedTask;

    public Task UpdateCellSourceAsync(Guid cellId, string source) => Task.CompletedTask;

    public Task ChangeCellTypeAsync(Guid cellId, string newType) => Task.CompletedTask;

    public Task ClearAllOutputsAsync() => Task.CompletedTask;

    // ── Execution ──────────────────────────────────────────────────────

    public Task<ExecutionResultDto> ExecuteCellAsync(Guid cellId)
    {
        ExecutedCellIds.Add(cellId);
        return Task.FromResult(new ExecutionResultDto(cellId, "ok", TimeSpan.FromMilliseconds(42)));
    }

    public Task<IReadOnlyList<ExecutionResultDto>> ExecuteAllAsync()
    {
        ExecuteAllCallCount++;
        return Task.FromResult<IReadOnlyList<ExecutionResultDto>>(new List<ExecutionResultDto>());
    }

    public Task RestartKernelAsync()
    {
        RestartKernelCallCount++;
        return Task.CompletedTask;
    }

    // ── Toolbar actions ────────────────────────────────────────────────

    public IReadOnlyList<ToolbarActionInfo> GetToolbarActions(ToolbarPlacement placement)
        => ToolbarActions.Where(a => a.Placement == placement).ToList();

    public Task<Dictionary<string, bool>> GetActionEnabledStatesAsync(
        ToolbarPlacement placement, IReadOnlyList<Guid> selectedCellIds)
        => Task.FromResult(ActionEnabledStates);

    public Task ExecuteActionAsync(string actionId, IReadOnlyList<Guid> selectedCellIds) => Task.CompletedTask;

    // ── Editor intelligence ────────────────────────────────────────────

    public Task<HoverResultDto?> GetHoverInfoAsync(Guid cellId, string code, int position)
        => Task.FromResult<HoverResultDto?>(null);

    public Task<CompletionsResultDto?> GetCompletionsAsync(Guid cellId, string code, int position)
        => Task.FromResult<CompletionsResultDto?>(null);

    // ── Layout & theme switching ───────────────────────────────────────

    public Task SwitchLayoutAsync(string layoutId)
    {
        SwitchLayoutCalls.Add(layoutId);
        ActiveLayoutId = layoutId;
        return Task.CompletedTask;
    }

    public Task SwitchThemeAsync(string themeId)
    {
        SwitchThemeCalls.Add(themeId);
        ActiveThemeId = themeId;
        return Task.CompletedTask;
    }

    // ── Extension management ───────────────────────────────────────────

    public Task EnableExtensionAsync(string extensionId)
    {
        EnableExtensionCalls.Add(extensionId);
        return Task.CompletedTask;
    }

    public Task DisableExtensionAsync(string extensionId)
    {
        DisableExtensionCalls.Add(extensionId);
        return Task.CompletedTask;
    }

    // ── Settings ───────────────────────────────────────────────────────

    public IReadOnlyList<ExtensionSettingsGroup> GetSettingDefinitions() => SettingDefinitions;

    public object? GetSettingValue(string extensionId, string settingName) => null;

    public Task UpdateSettingAsync(string extensionId, string settingName, object? value)
    {
        UpdateSettingCalls.Add((extensionId, settingName, value));
        return Task.CompletedTask;
    }

    // ── Variables ──────────────────────────────────────────────────────

    public IReadOnlyList<VariableEntryDto> GetVariables() => Variables;

    public Task<VariableInspectResultDto?> InspectVariableAsync(string name)
        => Task.FromResult(InspectResult);

    // ── Dashboard layout ───────────────────────────────────────────────

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId)
        => Task.FromResult(CellContainers.GetValueOrDefault(cellId,
            new CellContainerInfo(cellId, 0, 0, 6, 4)));

    public Task UpdateCellPositionAsync(Guid cellId, int row, int col, int colSpan, int rowSpan)
        => Task.CompletedTask;

    // ── Cell type helpers ──────────────────────────────────────────────

    public bool ShouldCollapseInput(string cellType)
        => CollapseInputMap.GetValueOrDefault(cellType, false);

    // ── Event raisers for tests ────────────────────────────────────────

    public void RaiseCellExecuted() => OnCellExecuted?.Invoke();
    public void RaiseNotebookChanged() => OnNotebookChanged?.Invoke();
    public void RaiseLayoutChanged() => OnLayoutChanged?.Invoke();
    public void RaiseThemeChanged() => OnThemeChanged?.Invoke();
    public void RaiseExtensionStatusChanged() => OnExtensionStatusChanged?.Invoke();
    public void RaiseVariablesChanged() => OnVariablesChanged?.Invoke();
    public void RaiseSettingsChanged() => OnSettingsChanged?.Invoke();
}
