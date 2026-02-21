using Microsoft.JSInterop;
using Verso.Abstractions;
using Verso.Blazor.Shared.Models;
using Verso.Blazor.Shared.Services;
using Verso.Contexts;
using Verso.Execution;
using Verso.Extensions;
using Verso.Extensions.Layouts;
using Verso.Serializers;

namespace Verso.Blazor.Services;

/// <summary>
/// In-process implementation of <see cref="INotebookService"/> for Blazor Server.
/// Wraps Scaffold + ExtensionHost, projecting engine types through the interface surface.
/// </summary>
public sealed class ServerNotebookService : INotebookService, IAsyncDisposable
{
    private Scaffold? _scaffold;
    private ExtensionHost? _extensionHost;
    private string? _filePath;
    private readonly IJSRuntime _jsRuntime;

    public ServerNotebookService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    // ── State ──────────────────────────────────────────────────────────

    public bool IsLoaded => _scaffold is not null;
    public bool IsEmbedded => false;
    public string? FilePath => _filePath;

    // ── Notebook metadata ──────────────────────────────────────────────

    public string? Title
    {
        get => _scaffold?.Title;
        set { if (_scaffold is not null) _scaffold.Title = value; }
    }

    public string? DefaultKernelId
    {
        get => _scaffold?.DefaultKernelId;
        set { if (_scaffold is not null) _scaffold.DefaultKernelId = value; }
    }

    public IReadOnlyList<string> RegisteredLanguages =>
        _scaffold?.RegisteredLanguages ?? Array.Empty<string>();

    public DateTimeOffset? Created => _scaffold?.Notebook.Created;
    public DateTimeOffset? Modified => _scaffold?.Notebook.Modified;
    public string FormatVersion => _scaffold?.Notebook.FormatVersion ?? "1.0";

    // ── Cells ──────────────────────────────────────────────────────────

    public IReadOnlyList<CellModel> Cells =>
        _scaffold?.Cells ?? (IReadOnlyList<CellModel>)Array.Empty<CellModel>();

    // ── Layout & theme ─────────────────────────────────────────────────

    public bool IsDashboardLayout =>
        _scaffold?.LayoutManager?.ActiveLayout?.RequiresCustomRenderer == true;

    public ThemeKind? ActiveThemeKind =>
        _scaffold?.ThemeEngine?.ActiveTheme?.ThemeKind;

    public ThemeData? ActiveThemeData
    {
        get
        {
            var theme = _scaffold?.ThemeEngine?.ActiveTheme;
            if (theme is null) return null;
            return new ThemeData(
                theme.Colors ?? new ThemeColorTokens(),
                theme.Typography ?? new ThemeTypography(),
                theme.Spacing ?? new ThemeSpacing());
        }
    }

    public string? ActiveLayoutId =>
        _scaffold?.LayoutManager?.ActiveLayout?.LayoutId;

    public string? ActiveThemeId =>
        _scaffold?.ThemeEngine?.ActiveTheme?.ThemeId;

    // ── Extension data ─────────────────────────────────────────────────

    public IReadOnlyList<CellTypeInfo> AvailableCellTypes
    {
        get
        {
            var types = new List<CellTypeInfo> { new("code", "Code") };
            if (_extensionHost is null) return types;

            var hasMarkdown = _extensionHost.GetCellTypes()
                .Any(ct => string.Equals(ct.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase))
                || _extensionHost.GetRenderers()
                .Any(r => string.Equals(r.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase));
            if (hasMarkdown)
                types.Add(new("markdown", "Markdown"));

            foreach (var ct in _extensionHost.GetCellTypes())
            {
                if (!string.Equals(ct.CellTypeId, "code", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ct.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase))
                    types.Add(new CellTypeInfo(ct.CellTypeId, ct.DisplayName));
            }

            return types;
        }
    }

    public IReadOnlyList<LayoutInfo> AvailableLayouts =>
        _extensionHost?.GetLayouts()
            .Select(l => new LayoutInfo(l.LayoutId, l.DisplayName, l.RequiresCustomRenderer))
            .ToList()
        ?? (IReadOnlyList<LayoutInfo>)Array.Empty<LayoutInfo>();

    public IReadOnlyList<ThemeInfo> AvailableThemes =>
        _extensionHost?.GetThemes()
            .Select(t => new ThemeInfo(t.ThemeId, t.DisplayName, t.ThemeKind))
            .ToList()
        ?? (IReadOnlyList<ThemeInfo>)Array.Empty<ThemeInfo>();

    public IReadOnlyList<ExtensionInfo> Extensions =>
        _extensionHost?.GetExtensionInfos()
        ?? (IReadOnlyList<ExtensionInfo>)Array.Empty<ExtensionInfo>();

    // ── Events ─────────────────────────────────────────────────────────

    public event Action? OnCellExecuted;
    public event Action? OnNotebookChanged;
    public event Action? OnLayoutChanged;
    public event Action? OnThemeChanged;
    public event Action? OnExtensionStatusChanged;
    public event Action? OnVariablesChanged;
    public event Action? OnSettingsChanged;

    // ── File operations ────────────────────────────────────────────────

    public async Task NewNotebookAsync()
    {
        await DisposeCurrentAsync();

        var notebook = new NotebookModel
        {
            Title = "Untitled",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            DefaultKernelId = "csharp"
        };

        _extensionHost = new ExtensionHost();
        await _extensionHost.LoadBuiltInExtensionsAsync();

        _scaffold = new Scaffold(notebook, _extensionHost);
        _scaffold.InitializeSubsystems();
        EnsureDefaults();
        _scaffold.AddCell("code", "csharp");
        _filePath = null;
        SubscribeToEngineEvents();

        OnNotebookChanged?.Invoke();
    }

    public async Task OpenAsync(string filePath)
    {
        await DisposeCurrentAsync();

        _extensionHost = new ExtensionHost();
        await _extensionHost.LoadBuiltInExtensionsAsync();

        var content = await File.ReadAllTextAsync(filePath);

        var serializer = _extensionHost.GetSerializers()
            .FirstOrDefault(s => s.CanImport(filePath))
            ?? (INotebookSerializer)new VersoSerializer();

        var notebook = await serializer.DeserializeAsync(content);

        _scaffold = new Scaffold(notebook, _extensionHost, filePath);
        _scaffold.InitializeSubsystems();
        EnsureDefaults();
        await RestoreLayoutMetadataAsync();
        await RestoreSettingsAsync();
        _filePath = filePath;
        SubscribeToEngineEvents();

        OnNotebookChanged?.Invoke();
    }

    public async Task OpenFromContentAsync(string fileName, string content)
    {
        var resolvedPath = await TryResolveFilePathAsync(fileName, content);
        if (resolvedPath is not null)
        {
            await OpenAsync(resolvedPath);
            return;
        }

        await DisposeCurrentAsync();

        _extensionHost = new ExtensionHost();
        await _extensionHost.LoadBuiltInExtensionsAsync();

        var serializer = _extensionHost.GetSerializers()
            .FirstOrDefault(s => s.CanImport(fileName))
            ?? (INotebookSerializer)new VersoSerializer();

        var notebook = await serializer.DeserializeAsync(content);

        _scaffold = new Scaffold(notebook, _extensionHost);
        _scaffold.InitializeSubsystems();
        EnsureDefaults();
        await RestoreLayoutMetadataAsync();
        await RestoreSettingsAsync();
        _filePath = null;
        SubscribeToEngineEvents();

        OnNotebookChanged?.Invoke();
    }

    public async Task SaveAsync(string filePath)
    {
        if (_scaffold is null) return;

        var json = await PrepareSerializedContentAsync();
        await File.WriteAllTextAsync(filePath, json);
        _filePath = filePath;
    }

    public async Task<string?> GetSerializedContentAsync()
    {
        if (_scaffold is null) return null;
        return await PrepareSerializedContentAsync();
    }

    private async Task<string> PrepareSerializedContentAsync()
    {
        if (_scaffold!.LayoutManager is { } lm)
            await lm.SaveMetadataAsync(_scaffold.Notebook);

        if (_scaffold.SettingsManager is { } sm)
            await sm.SaveSettingsAsync(_scaffold.Notebook);

        _scaffold.Notebook.Modified = DateTimeOffset.UtcNow;
        var serializer = new VersoSerializer();
        return await serializer.SerializeAsync(_scaffold.Notebook);
    }

    // ── Cell operations ────────────────────────────────────────────────

    public Task<CellModel> AddCellAsync(string type = "code", string? language = null)
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var effectiveLanguage = ResolveLanguage(type, language);
        var cell = _scaffold.AddCell(type, effectiveLanguage);
        OnNotebookChanged?.Invoke();
        return Task.FromResult(cell);
    }

    public Task<CellModel> InsertCellAsync(int index, string type = "code", string? language = null)
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var effectiveLanguage = ResolveLanguage(type, language);
        var cell = _scaffold.InsertCell(index, type, effectiveLanguage);
        OnNotebookChanged?.Invoke();
        return Task.FromResult(cell);
    }

    public Task<bool> RemoveCellAsync(Guid cellId)
    {
        if (_scaffold is null) return Task.FromResult(false);
        var result = _scaffold.RemoveCell(cellId);
        if (result) OnNotebookChanged?.Invoke();
        return Task.FromResult(result);
    }

    public Task MoveCellAsync(int fromIndex, int toIndex)
    {
        if (_scaffold is null) return Task.CompletedTask;
        _scaffold.MoveCell(fromIndex, toIndex);
        OnNotebookChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task UpdateCellSourceAsync(Guid cellId, string source)
    {
        _scaffold?.UpdateCellSource(cellId, source);
        return Task.CompletedTask;
    }

    public Task ChangeCellTypeAsync(Guid cellId, string newType)
    {
        if (_scaffold is null) return Task.CompletedTask;

        var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is null) return Task.CompletedTask;

        if (string.Equals(cell.Type, newType, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var effectiveLanguage = ResolveLanguage(newType, null);
        cell.Type = newType;
        cell.Language = effectiveLanguage;
        cell.Outputs.Clear();

        OnNotebookChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task ClearAllOutputsAsync()
    {
        _scaffold?.ClearAllOutputs();
        OnCellExecuted?.Invoke();
        return Task.CompletedTask;
    }

    // ── Execution ──────────────────────────────────────────────────────

    public async Task<ExecutionResultDto> ExecuteCellAsync(Guid cellId)
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var result = await _scaffold.ExecuteCellAsync(cellId);
        OnCellExecuted?.Invoke();
        return new ExecutionResultDto(result.CellId, result.Status.ToString(), result.Elapsed);
    }

    public async Task<IReadOnlyList<ExecutionResultDto>> ExecuteAllAsync()
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var results = await _scaffold.ExecuteAllAsync();
        OnCellExecuted?.Invoke();
        return results.Select(r =>
            new ExecutionResultDto(r.CellId, r.Status.ToString(), r.Elapsed)).ToList();
    }

    public async Task RestartKernelAsync()
    {
        if (_scaffold is null) return;
        await _scaffold.RestartKernelAsync();
    }

    // ── Toolbar actions ────────────────────────────────────────────────

    public IReadOnlyList<ToolbarActionInfo> GetToolbarActions(ToolbarPlacement placement)
    {
        if (_extensionHost is null) return Array.Empty<ToolbarActionInfo>();

        return _extensionHost.GetToolbarActions()
            .Where(a => a.Placement == placement)
            .OrderBy(a => a.Order)
            .Select(a => new ToolbarActionInfo(a.ActionId, a.DisplayName, a.Icon, a.Placement, a.Order))
            .ToList();
    }

    public async Task<Dictionary<string, bool>> GetActionEnabledStatesAsync(
        ToolbarPlacement placement, IReadOnlyList<Guid> selectedCellIds)
    {
        if (_scaffold is null || _extensionHost is null)
            return new Dictionary<string, bool>();

        var context = new BlazorToolbarActionContext(_scaffold, selectedCellIds, _jsRuntime);
        var actions = _extensionHost.GetToolbarActions()
            .Where(a => a.Placement == placement)
            .ToList();

        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in actions)
        {
            states[action.ActionId] = await action.IsEnabledAsync(context);
        }
        return states;
    }

    public async Task ExecuteActionAsync(string actionId, IReadOnlyList<Guid> selectedCellIds)
    {
        if (_scaffold is null || _extensionHost is null) return;

        var action = _extensionHost.GetToolbarActions()
            .FirstOrDefault(a => string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (action is null) return;

        var context = new BlazorToolbarActionContext(_scaffold, selectedCellIds, _jsRuntime);
        await action.ExecuteAsync(context);
    }

    // ── Editor intelligence ────────────────────────────────────────────

    public async Task<HoverResultDto?> GetHoverInfoAsync(Guid cellId, string code, int position)
    {
        if (_scaffold is null) return null;

        var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell?.Language is null) return null;

        var kernel = _scaffold.GetKernel(cell.Language);
        if (kernel is null) return null;

        var info = await kernel.GetHoverInfoAsync(code, position);
        if (info is null) return null;

        return new HoverResultDto(
            info.Content,
            info.Range is { } r
                ? new HoverRangeDto(r.StartLine, r.StartColumn, r.EndLine, r.EndColumn)
                : null);
    }

    public async Task<CompletionsResultDto?> GetCompletionsAsync(Guid cellId, string code, int position)
    {
        if (_scaffold is null) return null;

        var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell?.Language is null) return null;

        var kernel = _scaffold.GetKernel(cell.Language);
        if (kernel is null) return null;

        var completions = await kernel.GetCompletionsAsync(code, position);
        return new CompletionsResultDto(
            completions.Select(c => new CompletionItemDto(
                c.DisplayText, c.InsertText, c.Kind, c.Description, c.SortText)).ToList());
    }

    // ── Layout & theme switching ───────────────────────────────────────

    public Task SwitchLayoutAsync(string layoutId)
    {
        if (_scaffold?.LayoutManager is null) return Task.CompletedTask;
        _scaffold.LayoutManager.SetActiveLayout(layoutId);
        _scaffold.Notebook.ActiveLayoutId = layoutId;
        OnLayoutChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task SwitchThemeAsync(string themeId)
    {
        if (_scaffold?.ThemeEngine is null) return Task.CompletedTask;
        _scaffold.ThemeEngine.SetActiveTheme(themeId);
        _scaffold.Notebook.PreferredThemeId = themeId;
        OnThemeChanged?.Invoke();
        return Task.CompletedTask;
    }

    // ── Extension management ───────────────────────────────────────────

    public async Task EnableExtensionAsync(string extensionId)
    {
        if (_extensionHost is null) return;
        await _extensionHost.EnableExtensionAsync(extensionId);
    }

    public async Task DisableExtensionAsync(string extensionId)
    {
        if (_extensionHost is null) return;
        await _extensionHost.DisableExtensionAsync(extensionId);
    }

    // ── Settings ───────────────────────────────────────────────────────

    public IReadOnlyList<ExtensionSettingsGroup> GetSettingDefinitions()
    {
        if (_scaffold?.SettingsManager is null)
            return Array.Empty<ExtensionSettingsGroup>();

        return _scaffold.SettingsManager.GetAllDefinitions()
            .Select(d => new ExtensionSettingsGroup(d.ExtensionId, d.Definitions))
            .ToList();
    }

    public object? GetSettingValue(string extensionId, string settingName)
    {
        if (_extensionHost is null) return null;

        var ext = _extensionHost.GetSettableExtensions()
            .FirstOrDefault(e => e is IExtension ie &&
                string.Equals(ie.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase));

        if (ext is not null)
        {
            var values = ext.GetSettingValues();
            if (values.TryGetValue(settingName, out var val))
                return val;
        }

        return null;
    }

    public async Task UpdateSettingAsync(string extensionId, string settingName, object? value)
    {
        if (_scaffold?.SettingsManager is null) return;
        await _scaffold.SettingsManager.UpdateSettingAsync(extensionId, settingName, value);
    }

    // ── Variables ──────────────────────────────────────────────────────

    public IReadOnlyList<VariableEntryDto> GetVariables()
    {
        if (_scaffold is null || _extensionHost is null)
            return Array.Empty<VariableEntryDto>();

        var previewService = new VariablePreviewService(_extensionHost);
        var variables = _scaffold.Variables.GetAll();

        return variables.Where(v => !v.Name.StartsWith("__")).Select(v => new VariableEntryDto(
            v.Name,
            v.Type.Name,
            previewService.GetPreview(v.Value),
            v.Value is not null && v.Value is not string &&
                (v.Value is System.Collections.IEnumerable || v.Value.GetType().GetProperties().Length > 0)
        )).ToList();
    }

    public async Task<VariableInspectResultDto?> InspectVariableAsync(string name)
    {
        if (_scaffold is null || _extensionHost is null) return null;

        var variables = _scaffold.Variables;
        var all = variables.GetAll();
        var descriptor = all.FirstOrDefault(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        if (descriptor?.Value is null)
            return new VariableInspectResultDto(name, "null", "text/plain", "null");

        var formatters = _extensionHost.GetFormatters();
        var context = new SimpleFormatterContext(_extensionHost, variables);

        foreach (var formatter in formatters.OrderByDescending(f => f.Priority))
        {
            if (formatter.CanFormat(descriptor.Value, context))
            {
                try
                {
                    var output = await formatter.FormatAsync(descriptor.Value, context);
                    return new VariableInspectResultDto(
                        name, descriptor.Type.Name, output.MimeType, output.Content);
                }
                catch { /* fall through */ }
            }
        }

        return new VariableInspectResultDto(
            name, descriptor.Type.Name, "text/plain",
            descriptor.Value.ToString() ?? "null");
    }

    // ── Dashboard layout ───────────────────────────────────────────────

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId)
    {
        var layout = _scaffold?.LayoutManager?.ActiveLayout;
        if (layout is null)
            return Task.FromResult(new CellContainerInfo(cellId, 0, 0, 6, 4));

        var context = new BlazorToolbarActionContext(_scaffold!, new List<Guid>());
        return layout.GetCellContainerAsync(cellId, context);
    }

    public Task UpdateCellPositionAsync(Guid cellId, int row, int col, int colSpan, int rowSpan)
    {
        var layout = _scaffold?.LayoutManager?.ActiveLayout;
        if (layout is DashboardLayout dashboard)
            dashboard.UpdateCellPosition(cellId, row, col, colSpan, rowSpan);
        return Task.CompletedTask;
    }

    // ── Cell type helpers ──────────────────────────────────────────────

    public bool ShouldCollapseInput(string cellType)
    {
        var renderer = _extensionHost?.GetRenderers()
            .FirstOrDefault(r => string.Equals(r.CellTypeId, cellType, StringComparison.OrdinalIgnoreCase));
        return renderer?.CollapsesInputOnExecute ?? false;
    }

    // ── Private helpers ────────────────────────────────────────────────

    private string? ResolveLanguage(string type, string? language)
    {
        var effectiveLanguage = language;
        if (effectiveLanguage is null)
        {
            var cellType = _extensionHost?.GetCellTypes()
                .FirstOrDefault(t => string.Equals(t.CellTypeId, type, StringComparison.OrdinalIgnoreCase));

            if (cellType is not null)
                effectiveLanguage = cellType.Kernel?.LanguageId;
            else if (!HasRenderer(type))
                effectiveLanguage = _scaffold?.DefaultKernelId ?? "csharp";
        }
        return effectiveLanguage;
    }

    private bool HasRenderer(string type)
    {
        return _extensionHost?.GetRenderers()
            .Any(r => string.Equals(r.CellTypeId, type, StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    private void EnsureDefaults()
    {
        if (_scaffold is null || _extensionHost is null) return;

        if (_scaffold.LayoutManager is { ActiveLayout: null } lm)
        {
            var defaultLayout = lm.AvailableLayouts
                .FirstOrDefault(l => !l.RequiresCustomRenderer)
                ?? lm.AvailableLayouts.FirstOrDefault();
            if (defaultLayout is not null)
                lm.SetActiveLayout(defaultLayout.LayoutId);
        }

        if (_scaffold.ThemeEngine is { ActiveTheme: null } te)
        {
            var themes = _extensionHost.GetThemes();
            var defaultTheme = themes.FirstOrDefault(t => t.ThemeKind == ThemeKind.Light)
                ?? themes.FirstOrDefault();
            if (defaultTheme is not null)
                te.SetActiveTheme(defaultTheme.ThemeId);
        }
    }

    private async Task RestoreLayoutMetadataAsync()
    {
        if (_scaffold?.LayoutManager is not { } lm) return;
        if (_scaffold.Notebook.Layouts.Count == 0) return;

        var context = new BlazorToolbarActionContext(_scaffold, Array.Empty<Guid>());
        await lm.RestoreMetadataAsync(_scaffold.Notebook, context);
    }

    private async Task RestoreSettingsAsync()
    {
        if (_scaffold?.SettingsManager is not { } sm) return;
        if (_scaffold.Notebook.ExtensionSettings.Count == 0) return;
        await sm.RestoreSettingsAsync(_scaffold.Notebook);
    }

    private async Task<string?> TryResolveFilePathAsync(string fileName, string content)
    {
        var searchRoots = new List<string>();

        if (_filePath is not null)
        {
            var lastDir = Path.GetDirectoryName(_filePath);
            if (lastDir is not null && Directory.Exists(lastDir))
                searchRoots.Add(lastDir);
        }

        var cwd = Directory.GetCurrentDirectory();
        if (!searchRoots.Contains(cwd, StringComparer.Ordinal))
            searchRoots.Add(cwd);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 5,
            IgnoreInaccessible = true
        };

        foreach (var root in searchRoots)
        {
            foreach (var candidate in Directory.EnumerateFiles(root, fileName, options))
            {
                try
                {
                    var diskContent = await File.ReadAllTextAsync(candidate);
                    if (string.Equals(diskContent, content, StringComparison.Ordinal))
                        return Path.GetFullPath(candidate);
                }
                catch (IOException) { }
            }
        }

        return null;
    }

    private void SubscribeToEngineEvents()
    {
        if (_extensionHost is not null)
            _extensionHost.OnExtensionStatusChanged += HandleExtensionStatusChanged;

        if (_scaffold?.Variables is VariableStore vs)
            vs.OnVariablesChanged += HandleVariablesChanged;

        if (_scaffold?.SettingsManager is { } sm)
            sm.OnSettingsChanged += HandleSettingsChanged;
    }

    private void UnsubscribeFromEngineEvents()
    {
        if (_extensionHost is not null)
            _extensionHost.OnExtensionStatusChanged -= HandleExtensionStatusChanged;

        if (_scaffold?.Variables is VariableStore vs)
            vs.OnVariablesChanged -= HandleVariablesChanged;

        if (_scaffold?.SettingsManager is { } sm)
            sm.OnSettingsChanged -= HandleSettingsChanged;
    }

    private void HandleExtensionStatusChanged(string extensionId, ExtensionStatus status)
        => OnExtensionStatusChanged?.Invoke();

    private void HandleVariablesChanged()
        => OnVariablesChanged?.Invoke();

    private void HandleSettingsChanged(string extensionId, string settingName, object? value)
        => OnSettingsChanged?.Invoke();

    private async Task DisposeCurrentAsync()
    {
        UnsubscribeFromEngineEvents();
        if (_scaffold is not null)
        {
            await _scaffold.DisposeAsync();
            _scaffold = null;
        }
        _extensionHost = null;
        _filePath = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeCurrentAsync();
    }
}
