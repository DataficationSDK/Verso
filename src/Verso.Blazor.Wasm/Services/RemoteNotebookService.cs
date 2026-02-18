using System.Text.Json;
using Verso.Abstractions;
using Verso.Blazor.Shared.Models;
using Verso.Blazor.Shared.Services;

namespace Verso.Blazor.Wasm.Services;

/// <summary>
/// Implements <see cref="INotebookService"/> for Blazor WebAssembly by relaying all
/// operations to the Verso.Host process through the VS Code postMessage ↔ JSON-RPC bridge.
/// Maintains a local state cache that is populated on open and updated from responses/notifications.
/// </summary>
public sealed class RemoteNotebookService : INotebookService, IAsyncDisposable
{
    private readonly VsCodeBridge _bridge;

    // ── Local cache ─────────────────────────────────────────────────────
    private bool _isLoaded;
    private string? _filePath;
    private string? _title;
    private string? _defaultKernelId;
    private List<string> _registeredLanguages = new();
    private DateTimeOffset? _created;
    private DateTimeOffset? _modified;
    private string _formatVersion = "";
    private List<CellModel> _cells = new();
    private List<CellTypeInfo> _cellTypes = new();
    private List<ToolbarActionInfo> _toolbarActions = new();
    private List<LayoutInfo> _layouts = new();
    private List<ThemeInfo> _themes = new();
    private List<ExtensionInfo> _extensions = new();
    private string? _activeLayoutId;
    private string? _activeThemeId;
    private ThemeKind? _activeThemeKind;
    private bool _isDashboardLayout;

    // ── Debounce for cell source updates ────────────────────────────────
    private readonly Dictionary<Guid, CancellationTokenSource> _debounceCts = new();
    private const int DebounceDelayMs = 250;

    // ── Events ──────────────────────────────────────────────────────────
    public event Action? OnCellExecuted;
    public event Action? OnNotebookChanged;
    public event Action? OnLayoutChanged;
    public event Action? OnThemeChanged;
    public event Action? OnExtensionStatusChanged;
    public event Action? OnVariablesChanged;
    public event Action? OnSettingsChanged;

    public RemoteNotebookService(VsCodeBridge bridge)
    {
        _bridge = bridge;
        _bridge.OnNotification += HandleNotification;
    }

    // ── State properties ────────────────────────────────────────────────

    public bool IsLoaded => _isLoaded;
    public bool IsEmbedded => true;
    public string? FilePath => _filePath;

    // ── Notebook metadata ───────────────────────────────────────────────

    public string? Title
    {
        get => _title;
        set => _title = value;
    }

    public string? DefaultKernelId
    {
        get => _defaultKernelId;
        set => _defaultKernelId = value;
    }

    public IReadOnlyList<string> RegisteredLanguages => _registeredLanguages;
    public DateTimeOffset? Created => _created;
    public DateTimeOffset? Modified => _modified;
    public string FormatVersion => _formatVersion;

    // ── Cells ───────────────────────────────────────────────────────────

    public IReadOnlyList<CellModel> Cells => _cells;

    // ── Layout & theme ──────────────────────────────────────────────────

    public bool IsDashboardLayout => _isDashboardLayout;
    public ThemeKind? ActiveThemeKind => _activeThemeKind;
    public ThemeData? ActiveThemeData { get; private set; }
    public string? ActiveLayoutId => _activeLayoutId;
    public string? ActiveThemeId => _activeThemeId;

    // ── Extension data ──────────────────────────────────────────────────

    public IReadOnlyList<CellTypeInfo> AvailableCellTypes => _cellTypes;
    public IReadOnlyList<LayoutInfo> AvailableLayouts => _layouts;
    public IReadOnlyList<ThemeInfo> AvailableThemes => _themes;
    public IReadOnlyList<ExtensionInfo> Extensions => _extensions;

    // ── File operations ─────────────────────────────────────────────────

    public Task NewNotebookAsync()
    {
        // Not supported in embedded mode
        throw new NotSupportedException("New notebook is not available in embedded mode.");
    }

    public Task OpenAsync(string filePath)
    {
        // Not used directly in VS Code — the extension opens the file and sends content
        throw new NotSupportedException("Direct file open is not available in embedded mode.");
    }

    public async Task OpenFromContentAsync(string fileName, string content)
    {
        var result = await _bridge.RequestAsync<NotebookOpenResponse>(
            "notebook/open",
            new { content, filePath = fileName });

        _filePath = fileName;
        _title = result.Title;
        _defaultKernelId = result.DefaultKernel;
        _cells = result.Cells?.Select(MapCellFromDto).ToList() ?? new();
        _isLoaded = true;

        // Fetch supplementary data
        await RefreshExtensionDataAsync();
        await RefreshThemeDataAsync();

        OnNotebookChanged?.Invoke();
    }

    public async Task SaveAsync(string filePath)
    {
        // Get the serialized notebook content from the host
        var result = await _bridge.RequestAsync<SaveResponse>("notebook/save", null);

        // Route the content through the bridge for the VS Code extension to write to disk
        if (result.Content is not null)
        {
            await _bridge.RequestVoidAsync("extension/writeFile", new { content = result.Content, filePath });
        }
    }

    // ── Cell operations ─────────────────────────────────────────────────

    public async Task<CellModel> AddCellAsync(string type = "code", string? language = null)
    {
        var dto = await _bridge.RequestAsync<CellDto>(
            "cell/add",
            new { type, language, source = "" });

        var cell = MapCellFromDto(dto);
        _cells.Add(cell);
        OnNotebookChanged?.Invoke();
        return cell;
    }

    public async Task<CellModel> InsertCellAsync(int index, string type = "code", string? language = null)
    {
        var dto = await _bridge.RequestAsync<CellDto>(
            "cell/insert",
            new { index, type, language, source = "" });

        var cell = MapCellFromDto(dto);
        _cells.Insert(Math.Clamp(index, 0, _cells.Count), cell);
        OnNotebookChanged?.Invoke();
        return cell;
    }

    public async Task<bool> RemoveCellAsync(Guid cellId)
    {
        await _bridge.RequestVoidAsync("cell/remove", new { cellId = cellId.ToString() });
        var removed = _cells.RemoveAll(c => c.Id == cellId) > 0;
        if (removed) OnNotebookChanged?.Invoke();
        return removed;
    }

    public async Task MoveCellAsync(int fromIndex, int toIndex)
    {
        await _bridge.RequestVoidAsync("cell/move", new { fromIndex, toIndex });

        if (fromIndex >= 0 && fromIndex < _cells.Count)
        {
            var cell = _cells[fromIndex];
            _cells.RemoveAt(fromIndex);
            _cells.Insert(Math.Clamp(toIndex, 0, _cells.Count), cell);
        }

        OnNotebookChanged?.Invoke();
    }

    public async Task UpdateCellSourceAsync(Guid cellId, string source)
    {
        // Update local cache immediately for responsive UI
        var cell = _cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is not null)
            cell.Source = source;

        // Debounce the remote call to avoid flooding the bridge on every keystroke
        if (_debounceCts.TryGetValue(cellId, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _debounceCts[cellId] = cts;

        try
        {
            await Task.Delay(DebounceDelayMs, cts.Token);
            await _bridge.RequestVoidAsync("cell/updateSource", new { cellId = cellId.ToString(), source });
        }
        catch (TaskCanceledException)
        {
            // Debounced — a newer update superseded this one
        }
        finally
        {
            // Clean up if this is still the active CTS for this cell
            if (_debounceCts.TryGetValue(cellId, out var current) && current == cts)
                _debounceCts.Remove(cellId);
        }
    }

    public async Task ChangeCellTypeAsync(Guid cellId, string newType)
    {
        await _bridge.RequestVoidAsync("cell/changeType", new { cellId = cellId.ToString(), type = newType });
        // Re-fetch cell list after type change since the host may rebuild the cell
        await RefreshCellListAsync();
        OnNotebookChanged?.Invoke();
    }

    public async Task ClearAllOutputsAsync()
    {
        await _bridge.RequestVoidAsync("output/clearAll", null);

        foreach (var cell in _cells)
            cell.Outputs.Clear();

        OnCellExecuted?.Invoke();
    }

    // ── Execution ───────────────────────────────────────────────────────

    public async Task<ExecutionResultDto> ExecuteCellAsync(Guid cellId)
    {
        var result = await _bridge.RequestAsync<ExecutionResponse>(
            "execution/run",
            new { cellId = cellId.ToString() });

        // Update local cell outputs
        var cell = _cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is not null && result.Outputs is not null)
        {
            cell.Outputs.Clear();
            foreach (var o in result.Outputs)
                cell.Outputs.Add(MapOutputFromDto(o));
        }

        OnCellExecuted?.Invoke();

        return new ExecutionResultDto(
            cellId,
            result.Status ?? "completed",
            TimeSpan.FromMilliseconds(result.ElapsedMs));
    }

    public async Task<IReadOnlyList<ExecutionResultDto>> ExecuteAllAsync()
    {
        var result = await _bridge.RequestAsync<ExecutionResponse>(
            "execution/runAll", null);

        await RefreshCellListAsync();
        OnCellExecuted?.Invoke();

        return Array.Empty<ExecutionResultDto>();
    }

    public async Task RestartKernelAsync()
    {
        await _bridge.RequestVoidAsync("kernel/restart", null);
    }

    // ── Toolbar actions ─────────────────────────────────────────────────

    public IReadOnlyList<ToolbarActionInfo> GetToolbarActions(ToolbarPlacement placement)
    {
        return _toolbarActions.Where(a => a.Placement == placement).OrderBy(a => a.Order).ToList();
    }

    public async Task<Dictionary<string, bool>> GetActionEnabledStatesAsync(
        ToolbarPlacement placement, IReadOnlyList<Guid> selectedCellIds)
    {
        var result = await _bridge.RequestAsync<EnabledStatesResponse>(
            "toolbar/getEnabledStates",
            new
            {
                placement = placement.ToString(),
                selectedCellIds = selectedCellIds.Select(id => id.ToString()).ToList()
            });

        return result.States ?? new();
    }

    public async Task ExecuteActionAsync(string actionId, IReadOnlyList<Guid> selectedCellIds)
    {
        await _bridge.RequestVoidAsync(
            "toolbar/execute",
            new
            {
                actionId,
                selectedCellIds = selectedCellIds.Select(id => id.ToString()).ToList()
            });

        // Refresh state since actions can mutate cells, outputs, etc.
        await RefreshCellListAsync();
        OnCellExecuted?.Invoke();
    }

    // ── Editor intelligence ─────────────────────────────────────────────

    public async Task<HoverResultDto?> GetHoverInfoAsync(Guid cellId, string code, int position)
    {
        var result = await _bridge.RequestAsync<HoverResponse>(
            "kernel/getHoverInfo",
            new { cellId = cellId.ToString(), code, cursorPosition = position });

        if (result.Content is null) return null;

        return new HoverResultDto(
            result.Content,
            result.Range is not null
                ? new HoverRangeDto(result.Range.StartLine, result.Range.StartColumn, result.Range.EndLine, result.Range.EndColumn)
                : null);
    }

    public async Task<CompletionsResultDto?> GetCompletionsAsync(Guid cellId, string code, int position)
    {
        var result = await _bridge.RequestAsync<CompletionsResponse>(
            "kernel/getCompletions",
            new { cellId = cellId.ToString(), code, cursorPosition = position });

        if (result.Items is null || result.Items.Count == 0) return null;

        return new CompletionsResultDto(
            result.Items.Select(i => new CompletionItemDto(
                i.DisplayText, i.InsertText, i.Kind, i.Description, i.SortText)).ToList());
    }

    // ── Layout & theme switching ────────────────────────────────────────

    public async Task SwitchLayoutAsync(string layoutId)
    {
        await _bridge.RequestVoidAsync("layout/switch", new { layoutId });
        _activeLayoutId = layoutId;
        var layout = _layouts.FirstOrDefault(l => l.LayoutId == layoutId);
        _isDashboardLayout = layout?.RequiresCustomRenderer ?? false;
        OnLayoutChanged?.Invoke();
    }

    public async Task SwitchThemeAsync(string themeId)
    {
        await _bridge.RequestVoidAsync("theme/switch", new { themeId });
        _activeThemeId = themeId;
        var theme = _themes.FirstOrDefault(t => t.ThemeId == themeId);
        _activeThemeKind = theme?.ThemeKind;
        await RefreshThemeDataAsync();
        OnThemeChanged?.Invoke();
    }

    // ── Extension management ────────────────────────────────────────────

    public async Task EnableExtensionAsync(string extensionId)
    {
        await _bridge.RequestVoidAsync("extension/enable", new { extensionId });
        await RefreshExtensionDataAsync();
        OnExtensionStatusChanged?.Invoke();
    }

    public async Task DisableExtensionAsync(string extensionId)
    {
        await _bridge.RequestVoidAsync("extension/disable", new { extensionId });
        await RefreshExtensionDataAsync();
        OnExtensionStatusChanged?.Invoke();
    }

    // ── Settings ────────────────────────────────────────────────────────

    public IReadOnlyList<ExtensionSettingsGroup> GetSettingDefinitions()
    {
        // Settings are fetched lazily since they require a round-trip
        // For now, return cached or empty. A full refresh can be triggered via RefreshSettingsAsync.
        return _settingsCache;
    }

    public object? GetSettingValue(string extensionId, string settingName)
    {
        return _settingValues.TryGetValue($"{extensionId}:{settingName}", out var val) ? val : null;
    }

    public async Task UpdateSettingAsync(string extensionId, string settingName, object? value)
    {
        await _bridge.RequestVoidAsync("settings/update",
            new { extensionId, settingName, value = value?.ToString() });
        _settingValues[$"{extensionId}:{settingName}"] = value;
        OnSettingsChanged?.Invoke();
    }

    private List<ExtensionSettingsGroup> _settingsCache = new();
    private Dictionary<string, object?> _settingValues = new();

    // ── Variables ───────────────────────────────────────────────────────

    public IReadOnlyList<VariableEntryDto> GetVariables()
    {
        return _variablesCache;
    }

    public async Task<VariableInspectResultDto?> InspectVariableAsync(string name)
    {
        var result = await _bridge.RequestAsync<VariableInspectResponse>(
            "variable/inspect",
            new { name });

        if (result.Name is null) return null;

        return new VariableInspectResultDto(result.Name, result.TypeName ?? "", result.MimeType ?? "text/plain", result.Content ?? "");
    }

    private List<VariableEntryDto> _variablesCache = new();

    // ── Dashboard layout ────────────────────────────────────────────────

    public async Task<CellContainerInfo> GetCellContainerAsync(Guid cellId)
    {
        var result = await _bridge.RequestAsync<CellContainerResponse>(
            "layout/getCellContainer",
            new { cellId = cellId.ToString() });

        return new CellContainerInfo(cellId, result.Col, result.Row, result.Width, result.Height);
    }

    public async Task UpdateCellPositionAsync(Guid cellId, int row, int col, int colSpan, int rowSpan)
    {
        await _bridge.RequestVoidAsync("layout/updateCell",
            new { cellId = cellId.ToString(), row, col, width = colSpan, height = rowSpan });
    }

    // ── Cell type helpers ───────────────────────────────────────────────

    public bool ShouldCollapseInput(string cellType)
    {
        // In WASM, check if this cell type is a rendering cell (markdown, etc.)
        return string.Equals(cellType, "markdown", StringComparison.OrdinalIgnoreCase);
    }

    // ── Notification handling ───────────────────────────────────────────

    private void HandleNotification(string method, string? paramsJson)
    {
        switch (method)
        {
            case "notebook/opened":
                _ = HandleNotebookOpenedAsync(paramsJson);
                break;
            case "cell/executionState":
                OnCellExecuted?.Invoke();
                break;
            case "settings/changed":
                OnSettingsChanged?.Invoke();
                break;
            case "variable/changed":
                _ = RefreshVariablesAsync();
                break;
        }
    }

    private async Task HandleNotebookOpenedAsync(string? paramsJson)
    {
        if (paramsJson is null) return;

        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var result = JsonSerializer.Deserialize<NotebookOpenedNotification>(paramsJson, opts);
        if (result is null) return;

        _filePath = result.FilePath;
        _title = result.Title;
        _defaultKernelId = result.DefaultKernel;
        _cells = result.Cells?.Select(MapCellFromDto).ToList() ?? new();
        _isLoaded = true;

        // Fetch supplementary data (toolbar actions, languages, themes, etc.)
        await RefreshExtensionDataAsync();
        await RefreshThemeDataAsync();

        OnNotebookChanged?.Invoke();
    }

    // ── Internal refresh helpers ────────────────────────────────────────

    private async Task RefreshCellListAsync()
    {
        var result = await _bridge.RequestAsync<CellListResponse>("cell/list", null);
        _cells = result.Cells?.Select(MapCellFromDto).ToList() ?? new();
    }

    private async Task RefreshExtensionDataAsync()
    {
        // Toolbar actions
        var actionsResult = await _bridge.RequestAsync<ToolbarActionsResponse>("notebook/getToolbarActions", null);
        _toolbarActions = actionsResult.Actions?.Select(a => new ToolbarActionInfo(
            a.ActionId, a.DisplayName, a.Icon,
            Enum.TryParse<ToolbarPlacement>(a.Placement, true, out var p) ? p : ToolbarPlacement.MainToolbar,
            a.Order)).ToList() ?? new();

        // Cell types
        var cellTypesResult = await _bridge.RequestAsync<CellTypesResponse>("notebook/getCellTypes", null);
        _cellTypes = cellTypesResult.CellTypes?.Select(ct => new CellTypeInfo(ct.Id, ct.DisplayName)).ToList() ?? new();

        // Languages
        var langsResult = await _bridge.RequestAsync<LanguagesResponse>("notebook/getLanguages", null);
        _registeredLanguages = langsResult.Languages?.Select(l => l.Id).ToList() ?? new();

        // Layouts
        var layoutsResult = await _bridge.RequestAsync<LayoutsResponse>("layout/getLayouts", null);
        _layouts = layoutsResult.Layouts?.Select(l =>
        {
            if (l.IsActive) { _activeLayoutId = l.Id; _isDashboardLayout = l.RequiresCustomRenderer; }
            return new LayoutInfo(l.Id, l.DisplayName, l.RequiresCustomRenderer);
        }).ToList() ?? new();

        // Themes
        var themesResult = await _bridge.RequestAsync<ThemesResponse>("theme/getThemes", null);
        _themes = themesResult.Themes?.Select(t =>
        {
            var kind = Enum.TryParse<ThemeKind>(t.ThemeKind, true, out var k) ? k : ThemeKind.Light;
            if (t.IsActive) { _activeThemeId = t.Id; _activeThemeKind = kind; }
            return new ThemeInfo(t.Id, t.DisplayName, kind);
        }).ToList() ?? new();

        // Extensions
        var extsResult = await _bridge.RequestAsync<ExtensionsResponse>("extension/list", null);
        _extensions = extsResult.Extensions?.ToList() ?? new();

        // Settings
        await RefreshSettingsAsync();

        // Variables
        await RefreshVariablesAsync();
    }

    private async Task RefreshThemeDataAsync()
    {
        try
        {
            var theme = await _bridge.RequestAsync<ThemeDataResponse>("notebook/getTheme", null);
            if (theme.Colors is not null)
            {
                ActiveThemeData = new ThemeData(
                    MapColorsFromDict(theme.Colors),
                    MapTypographyFromDto(theme.Typography),
                    MapSpacingFromDto(theme.Spacing));
            }
        }
        catch
        {
            // Theme data is optional
        }
    }

    private async Task RefreshSettingsAsync()
    {
        try
        {
            var defs = await _bridge.RequestAsync<SettingsDefinitionsResponse>("settings/getDefinitions", null);
            _settingsCache = defs.Extensions?.Select(g => new ExtensionSettingsGroup(
                g.ExtensionId,
                g.Definitions?.Select(MapSettingDefinitionFromDto).ToList()
                    ?? new List<SettingDefinition>()
            )).ToList() ?? new();

            // Use currentValues from the response instead of N+1 individual queries
            _settingValues.Clear();
            foreach (var ext in defs.Extensions ?? Enumerable.Empty<SettingsExtensionDto>())
            {
                if (ext.CurrentValues is not null)
                {
                    foreach (var kv in ext.CurrentValues)
                        _settingValues[$"{ext.ExtensionId}:{kv.Key}"] = kv.Value;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RemoteNotebookService] RefreshSettingsAsync error: {ex.Message}");
        }
    }

    private static SettingDefinition MapSettingDefinitionFromDto(SettingDefinitionDto dto)
    {
        var settingType = Enum.TryParse<SettingType>(dto.SettingType, true, out var st)
            ? st : SettingType.String;

        SettingConstraints? constraints = null;
        if (dto.Constraints is not null)
        {
            constraints = new SettingConstraints(
                dto.Constraints.MinValue,
                dto.Constraints.MaxValue,
                dto.Constraints.Pattern,
                dto.Constraints.Choices,
                dto.Constraints.MaxLength,
                dto.Constraints.MaxItems);
        }

        return new SettingDefinition(
            dto.Name, dto.DisplayName, dto.Description, settingType,
            dto.DefaultValue, dto.Category, constraints, dto.Order);
    }

    private async Task RefreshVariablesAsync()
    {
        try
        {
            var result = await _bridge.RequestAsync<VariableListResponse>("variable/list", null);
            _variablesCache = result.Variables?.Select(v =>
                new VariableEntryDto(v.Name, v.TypeName, v.ValuePreview, v.IsExpandable)).ToList() ?? new();
            OnVariablesChanged?.Invoke();
        }
        catch { }
    }

    // ── DTO mapping ─────────────────────────────────────────────────────

    private static CellModel MapCellFromDto(CellDto dto)
    {
        var cell = new CellModel
        {
            Id = Guid.TryParse(dto.Id, out var id) ? id : Guid.NewGuid(),
            Type = dto.Type,
            Language = dto.Language,
            Source = dto.Source
        };
        if (dto.Outputs is not null)
        {
            foreach (var o in dto.Outputs)
                cell.Outputs.Add(MapOutputFromDto(o));
        }
        return cell;
    }

    private static CellOutput MapOutputFromDto(CellOutputDto dto)
    {
        return new CellOutput(dto.MimeType, dto.Content, dto.IsError, dto.ErrorName, dto.ErrorStackTrace);
    }

    private static ThemeColorTokens MapColorsFromDict(Dictionary<string, string> colors)
    {
        // Host sends camelCase keys; nameof() produces PascalCase — use case-insensitive lookup
        var ci = new Dictionary<string, string>(colors, StringComparer.OrdinalIgnoreCase);
        string G(string key, string fallback) =>
            ci.TryGetValue(key, out var v) ? v : fallback;

        var d = new ThemeColorTokens();
        return d with
        {
            EditorBackground = G(nameof(d.EditorBackground), d.EditorBackground),
            EditorForeground = G(nameof(d.EditorForeground), d.EditorForeground),
            EditorLineNumber = G(nameof(d.EditorLineNumber), d.EditorLineNumber),
            EditorCursor = G(nameof(d.EditorCursor), d.EditorCursor),
            EditorSelection = G(nameof(d.EditorSelection), d.EditorSelection),
            EditorGutter = G(nameof(d.EditorGutter), d.EditorGutter),
            EditorWhitespace = G(nameof(d.EditorWhitespace), d.EditorWhitespace),
            CellBackground = G(nameof(d.CellBackground), d.CellBackground),
            CellBorder = G(nameof(d.CellBorder), d.CellBorder),
            CellActiveBorder = G(nameof(d.CellActiveBorder), d.CellActiveBorder),
            CellHoverBackground = G(nameof(d.CellHoverBackground), d.CellHoverBackground),
            CellOutputBackground = G(nameof(d.CellOutputBackground), d.CellOutputBackground),
            CellOutputForeground = G(nameof(d.CellOutputForeground), d.CellOutputForeground),
            CellErrorBackground = G(nameof(d.CellErrorBackground), d.CellErrorBackground),
            CellErrorForeground = G(nameof(d.CellErrorForeground), d.CellErrorForeground),
            CellRunningIndicator = G(nameof(d.CellRunningIndicator), d.CellRunningIndicator),
            ToolbarBackground = G(nameof(d.ToolbarBackground), d.ToolbarBackground),
            ToolbarForeground = G(nameof(d.ToolbarForeground), d.ToolbarForeground),
            ToolbarButtonHover = G(nameof(d.ToolbarButtonHover), d.ToolbarButtonHover),
            ToolbarSeparator = G(nameof(d.ToolbarSeparator), d.ToolbarSeparator),
            ToolbarDisabledForeground = G(nameof(d.ToolbarDisabledForeground), d.ToolbarDisabledForeground),
            SidebarBackground = G(nameof(d.SidebarBackground), d.SidebarBackground),
            SidebarForeground = G(nameof(d.SidebarForeground), d.SidebarForeground),
            SidebarItemHover = G(nameof(d.SidebarItemHover), d.SidebarItemHover),
            SidebarItemActive = G(nameof(d.SidebarItemActive), d.SidebarItemActive),
            BorderDefault = G(nameof(d.BorderDefault), d.BorderDefault),
            BorderFocused = G(nameof(d.BorderFocused), d.BorderFocused),
            AccentPrimary = G(nameof(d.AccentPrimary), d.AccentPrimary),
            AccentSecondary = G(nameof(d.AccentSecondary), d.AccentSecondary),
            HighlightBackground = G(nameof(d.HighlightBackground), d.HighlightBackground),
            HighlightForeground = G(nameof(d.HighlightForeground), d.HighlightForeground),
            StatusSuccess = G(nameof(d.StatusSuccess), d.StatusSuccess),
            StatusWarning = G(nameof(d.StatusWarning), d.StatusWarning),
            StatusError = G(nameof(d.StatusError), d.StatusError),
            StatusInfo = G(nameof(d.StatusInfo), d.StatusInfo),
            ScrollbarThumb = G(nameof(d.ScrollbarThumb), d.ScrollbarThumb),
            ScrollbarTrack = G(nameof(d.ScrollbarTrack), d.ScrollbarTrack),
            ScrollbarThumbHover = G(nameof(d.ScrollbarThumbHover), d.ScrollbarThumbHover),
            OverlayBackground = G(nameof(d.OverlayBackground), d.OverlayBackground),
            OverlayBorder = G(nameof(d.OverlayBorder), d.OverlayBorder),
            DropdownBackground = G(nameof(d.DropdownBackground), d.DropdownBackground),
            DropdownHover = G(nameof(d.DropdownHover), d.DropdownHover),
            TooltipBackground = G(nameof(d.TooltipBackground), d.TooltipBackground),
            TooltipForeground = G(nameof(d.TooltipForeground), d.TooltipForeground),
        };
    }

    private static ThemeTypography MapTypographyFromDto(ThemeTypographyResponse? dto)
    {
        if (dto is null) return new ThemeTypography();
        return new ThemeTypography
        {
            EditorFont = MapFont(dto.EditorFont),
            UIFont = MapFont(dto.UIFont),
            ProseFont = MapFont(dto.ProseFont),
            CodeOutputFont = MapFont(dto.CodeOutputFont)
        };
    }

    private static FontDescriptor MapFont(FontResponse? f)
    {
        if (f is null) return new FontDescriptor("monospace", 14);
        return new FontDescriptor(f.Family ?? "monospace", f.SizePx, f.Weight, f.LineHeight);
    }

    private static ThemeSpacing MapSpacingFromDto(ThemeSpacingResponse? dto)
    {
        if (dto is null) return new ThemeSpacing();
        return new ThemeSpacing
        {
            CellPadding = dto.CellPadding,
            CellGap = dto.CellGap,
            ToolbarHeight = dto.ToolbarHeight,
            SidebarWidth = dto.SidebarWidth,
            ContentMarginHorizontal = dto.ContentMarginHorizontal,
            ContentMarginVertical = dto.ContentMarginVertical,
            CellBorderRadius = dto.CellBorderRadius,
            ButtonBorderRadius = dto.ButtonBorderRadius,
            OutputPadding = dto.OutputPadding,
            ScrollbarWidth = dto.ScrollbarWidth
        };
    }

    public ValueTask DisposeAsync()
    {
        _bridge.OnNotification -= HandleNotification;

        foreach (var cts in _debounceCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _debounceCts.Clear();

        return ValueTask.CompletedTask;
    }

    // ── Response DTOs (for deserialization from JSON-RPC) ───────────────

    private sealed class NotebookOpenResponse
    {
        public string? Title { get; set; }
        public string? DefaultKernel { get; set; }
        public List<CellDto>? Cells { get; set; }
    }

    private sealed class NotebookOpenedNotification
    {
        public string? FilePath { get; set; }
        public string? Title { get; set; }
        public string? DefaultKernel { get; set; }
        public List<CellDto>? Cells { get; set; }
    }

    private sealed class SaveResponse
    {
        public string? Content { get; set; }
    }

    private sealed class CellDto
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "code";
        public string? Language { get; set; }
        public string Source { get; set; } = "";
        public List<CellOutputDto>? Outputs { get; set; }
    }

    private sealed class CellOutputDto
    {
        public string MimeType { get; set; } = "";
        public string Content { get; set; } = "";
        public bool IsError { get; set; }
        public string? ErrorName { get; set; }
        public string? ErrorStackTrace { get; set; }
    }

    private sealed class CellListResponse
    {
        public List<CellDto>? Cells { get; set; }
    }

    private sealed class ExecutionResponse
    {
        public string? Status { get; set; }
        public double ElapsedMs { get; set; }
        public List<CellOutputDto>? Outputs { get; set; }
    }

    private sealed class CellTypesResponse
    {
        public List<CellTypeResponseDto>? CellTypes { get; set; }
    }

    private sealed class CellTypeResponseDto
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    private sealed class ToolbarActionsResponse
    {
        public List<ToolbarActionDto>? Actions { get; set; }
    }

    private sealed class ToolbarActionDto
    {
        public string ActionId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? Icon { get; set; }
        public string Placement { get; set; } = "";
        public int Order { get; set; }
    }

    private sealed class EnabledStatesResponse
    {
        public Dictionary<string, bool>? States { get; set; }
    }

    private sealed class LanguagesResponse
    {
        public List<LanguageItem>? Languages { get; set; }
    }

    private sealed class LanguageItem
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    private sealed class LayoutsResponse
    {
        public List<LayoutItem>? Layouts { get; set; }
    }

    private sealed class LayoutItem
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool RequiresCustomRenderer { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class ThemesResponse
    {
        public List<ThemeItem>? Themes { get; set; }
    }

    private sealed class ThemeItem
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ThemeKind { get; set; } = "";
        public bool IsActive { get; set; }
    }

    private sealed class ExtensionsResponse
    {
        public List<ExtensionInfo>? Extensions { get; set; }
    }

    private sealed class ThemeDataResponse
    {
        public Dictionary<string, string>? Colors { get; set; }
        public Dictionary<string, string>? SyntaxColors { get; set; }
        public ThemeTypographyResponse? Typography { get; set; }
        public ThemeSpacingResponse? Spacing { get; set; }
    }

    private sealed class ThemeTypographyResponse
    {
        public FontResponse? EditorFont { get; set; }
        public FontResponse? UIFont { get; set; }
        public FontResponse? ProseFont { get; set; }
        public FontResponse? CodeOutputFont { get; set; }
    }

    private sealed class FontResponse
    {
        public string? Family { get; set; }
        public double SizePx { get; set; }
        public int Weight { get; set; } = 400;
        public double LineHeight { get; set; } = 1.4;
    }

    private sealed class ThemeSpacingResponse
    {
        public double CellPadding { get; set; }
        public double CellGap { get; set; }
        public double ToolbarHeight { get; set; }
        public double SidebarWidth { get; set; }
        public double ContentMarginHorizontal { get; set; }
        public double ContentMarginVertical { get; set; }
        public double CellBorderRadius { get; set; }
        public double ButtonBorderRadius { get; set; }
        public double OutputPadding { get; set; }
        public double ScrollbarWidth { get; set; }
    }

    private sealed class HoverResponse
    {
        public string? Content { get; set; }
        public RangeResponse? Range { get; set; }
    }

    private sealed class RangeResponse
    {
        public int StartLine { get; set; }
        public int StartColumn { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
    }

    private sealed class CompletionsResponse
    {
        public List<CompletionItem>? Items { get; set; }
    }

    private sealed class CompletionItem
    {
        public string DisplayText { get; set; } = "";
        public string InsertText { get; set; } = "";
        public string? Kind { get; set; }
        public string? Description { get; set; }
        public string? SortText { get; set; }
    }

    private sealed class SettingsDefinitionsResponse
    {
        public List<SettingsExtensionDto>? Extensions { get; set; }
    }

    private sealed class SettingsExtensionDto
    {
        public string ExtensionId { get; set; } = "";
        public string ExtensionName { get; set; } = "";
        public List<SettingDefinitionDto>? Definitions { get; set; }
        public Dictionary<string, object?>? CurrentValues { get; set; }
    }

    private sealed class SettingDefinitionDto
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string SettingType { get; set; } = "";
        public object? DefaultValue { get; set; }
        public string? Category { get; set; }
        public SettingConstraintsDto? Constraints { get; set; }
        public int Order { get; set; }
    }

    private sealed class SettingConstraintsDto
    {
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public string? Pattern { get; set; }
        public List<string>? Choices { get; set; }
        public int? MaxLength { get; set; }
        public int? MaxItems { get; set; }
    }

    private sealed class SettingValueResponse
    {
        public object? Value { get; set; }
    }

    private sealed class VariableListResponse
    {
        public List<VariableItem>? Variables { get; set; }
    }

    private sealed class VariableItem
    {
        public string Name { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string ValuePreview { get; set; } = "";
        public bool IsExpandable { get; set; }
    }

    private sealed class VariableInspectResponse
    {
        public string? Name { get; set; }
        public string? TypeName { get; set; }
        public string? MimeType { get; set; }
        public string? Content { get; set; }
    }

    private sealed class CellContainerResponse
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
