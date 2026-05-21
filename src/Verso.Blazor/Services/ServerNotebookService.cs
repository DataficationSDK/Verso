using Microsoft.JSInterop;
using Verso.Abstractions;
using Verso.Blazor.Shared.Models;
using Verso.Blazor.Shared.Services;
using Verso.Contexts;
using Verso.Execution;
using Verso.Extensions;
using Verso.Extensions.Layouts;
using Verso.MagicCommands;
using Verso.Extensions.Utilities;
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
    private CancellationTokenSource? _executionCts;
    private readonly IJSRuntime _jsRuntime;
    private readonly NotebookServiceOptions _options;
    private readonly object _inputLock = new();
    private TaskCompletionSource<string?>? _inputTcs;
    private ServerInputRequest? _pendingInputRequest;
    // Monaco is eagerly loaded at page load (before any notebook opens),
    // so by the time cells render, define.amd is already removed and
    // output scripts cannot interfere with the AMD loader.


    // Extension consent state
    private TaskCompletionSource<bool>? _consentTcs;
    private IReadOnlyList<ExtensionConsentInfo>? _pendingConsentExtensions;

    /// <summary>Raised when extensions need user consent. The UI should show the consent dialog.</summary>
    public event Action? OnExtensionConsentRequested;

    /// <summary>Raised when kernel execution needs a single input value from the UI.</summary>
    public event Action? OnInputRequested;

    /// <summary>The extensions awaiting consent, if any.</summary>
    public IReadOnlyList<ExtensionConsentInfo>? PendingConsentExtensions => _pendingConsentExtensions;

    /// <summary>The pending execution input request, if any.</summary>
    public ServerInputRequest? PendingInputRequest
    {
        get
        {
            lock (_inputLock)
                return _pendingInputRequest;
        }
    }

    /// <summary>Called by the UI to resolve the pending consent request.</summary>
    public void ResolveConsentResult(bool approved)
    {
        _consentTcs?.TrySetResult(approved);
        _pendingConsentExtensions = null;
    }

    /// <summary>Called by the UI to resolve the pending execution input request.</summary>
    public void ResolveInputResult(string? value, bool cancelled)
    {
        TaskCompletionSource<string?>? tcs;
        lock (_inputLock)
        {
            tcs = _inputTcs;
            _inputTcs = null;
            _pendingInputRequest = null;
        }

        tcs?.TrySetResult(cancelled ? null : value ?? string.Empty);
    }

    public ServerNotebookService(IJSRuntime jsRuntime, NotebookServiceOptions? options = null)
    {
        _jsRuntime = jsRuntime;
        _options = options ?? new NotebookServiceOptions();
    }

    private async Task LoadExtensionsAsync()
    {
        await _extensionHost!.LoadBuiltInExtensionsAsync();

        var dir = _options.ExtensionsDirectory;
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            await _extensionHost.LoadFromDirectoryAsync(dir);
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

    public IReadOnlyList<KernelLanguageInfo> RegisteredLanguages
    {
        get
        {
            if (_scaffold is null) return Array.Empty<KernelLanguageInfo>();
            return _scaffold.RegisteredLanguages.Select(langId =>
            {
                var kernel = _scaffold.GetKernel(langId);
                return new KernelLanguageInfo(
                    langId,
                    kernel?.DisplayName ?? langId,
                    LanguageSupportsCancellation(langId));
            }).ToList();
        }
    }

    // Languages that complete synchronously in microseconds (string substitution
    // kernels) cannot be meaningfully cancelled; the UI suppresses the Stop button
    // for these. Mirrors NotebookHandler.LanguageSupportsCancellation in the host.
    private static bool LanguageSupportsCancellation(string languageId) =>
        !string.Equals(languageId, "html", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(languageId, "mermaid", StringComparison.OrdinalIgnoreCase);

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

    public LayoutReference? ActiveLayout
    {
        get
        {
            var layout = _scaffold?.LayoutManager?.ActiveLayout;
            if (layout is null) return null;
            var ext = (layout as IExtension)?.ExtensionId ?? string.Empty;
            return new LayoutReference(ext, layout.LayoutId);
        }
    }

    public string? ActiveLayoutRendererIsolation
    {
        get
        {
            var layout = _scaffold?.LayoutManager?.ActiveLayout;
            return layout?.RequiresCustomRenderer == true ? layout.RendererIsolation : null;
        }
    }

    public bool ActiveLayoutSupportsPropertiesPanel =>
        _scaffold?.LayoutManager?.ActiveLayout?.SupportsPropertiesPanel == true;

    public LayoutCapabilities LayoutCapabilities =>
        _scaffold?.LayoutCapabilities ?? (LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete |
                             LayoutCapabilities.CellReorder | LayoutCapabilities.CellEdit |
                             LayoutCapabilities.CellResize | LayoutCapabilities.CellExecute |
                             LayoutCapabilities.MultiSelect);

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
            .Select(l => new LayoutInfo(
                l.LayoutId,
                l.DisplayName,
                l.RequiresCustomRenderer,
                l.Capabilities,
                l.SupportsPropertiesPanel,
                (l as IExtension)?.ExtensionId ?? string.Empty,
                l.RendererIsolation))
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
    public event Action<Guid>? OnCellExecuting;
    public event Action<Guid>? OnCellExecutionCompleted;
    public event Action? OnNotebookChanged;
    public event Action? OnLayoutChanged;
#pragma warning disable CS0067 // Event never raised: server-side dispatch for layout/updated is not yet implemented.
    public event Action<LayoutUpdatedEventArgs>? OnLayoutUpdated;
#pragma warning restore CS0067
    public event Action? OnThemeChanged;
    public event Action? OnExtensionStatusChanged;
    public event Action? OnVariablesChanged;
    public event Action? OnSettingsChanged;
    public event Action? OnOutputUpdated;

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
        await LoadExtensionsAsync();
        WireConsentHandler();

        _scaffold = new Scaffold(notebook, _extensionHost);
        _scaffold.InitializeSubsystems();
        EnsureDefaults();
        _scaffold.AddCell("code", "csharp");
        _filePath = null;
        SubscribeToEngineEvents();
        WarmUpKernelsInBackground();

        OnNotebookChanged?.Invoke();
    }

    public async Task OpenAsync(string filePath)
    {
        await DisposeCurrentAsync();

        _extensionHost = new ExtensionHost();
        await LoadExtensionsAsync();
        WireConsentHandler();

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
        WarmUpKernelsInBackground();

        OnNotebookChanged?.Invoke();

        await ScanAndRequestConsentAsync(notebook);
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
        await LoadExtensionsAsync();
        WireConsentHandler();

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
        WarmUpKernelsInBackground();

        OnNotebookChanged?.Invoke();

        await ScanAndRequestConsentAsync(notebook);
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

    public Task ChangeCellLanguageAsync(Guid cellId, string newLanguage)
    {
        if (_scaffold is null) return Task.CompletedTask;

        var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is null) return Task.CompletedTask;

        if (string.Equals(cell.Language, newLanguage, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        cell.Language = newLanguage;
        cell.Outputs.Clear();

        // Eagerly warm up the target kernel so IntelliSense is ready immediately
        var scaffold = _scaffold;
        _ = Task.Run(async () =>
        {
            try { await scaffold!.WarmUpKernelAsync(newLanguage); }
            catch { /* warm-up failure is non-fatal */ }
        });

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

        // Per-cell events are forwarded from Scaffold via SubscribeToEngineEvents.
        var ct = ResetExecutionToken();
        try
        {
            var result = await _scaffold.ExecuteCellAsync(cellId, ct);
            return new ExecutionResultDto(result.CellId, result.Status.ToString(), result.ExecutionCount, result.Elapsed);
        }
        catch (OperationCanceledException)
        {
            // Surface cancellation as a normal Cancelled result so the UI doesn't
            // render a generic execution-error banner.
            return new ExecutionResultDto(cellId, "Cancelled", 0, TimeSpan.Zero);
        }
    }

    public async Task<IReadOnlyList<ExecutionResultDto>> ExecuteAllAsync()
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var ct = ResetExecutionToken();
        try
        {
            var results = await _scaffold.ExecuteAllAsync(ct);
            return results.Select(r =>
                new ExecutionResultDto(r.CellId, r.Status.ToString(), r.ExecutionCount, r.Elapsed)).ToList();
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<ExecutionResultDto>();
        }
    }

    public Task CancelCellAsync(Guid cellId)
    {
        _executionCts?.Cancel();
        return Task.CompletedTask;
    }

    public async Task RestartKernelAsync()
    {
        if (_scaffold is null) return;
        await _scaffold.RestartKernelAsync();
    }

    private CancellationToken ResetExecutionToken()
    {
        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        return _executionCts.Token;
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

    // ── Cell interaction ────────────────────────────────────────────────

    public async Task<string?> HandleCellInteractionAsync(
        Guid cellId, string extensionId, string interactionType,
        string payload, string? outputBlockId, CellRegion region)
    {
        if (_scaffold is null || _extensionHost is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var handler = _extensionHost.GetInteractionHandler(extensionId)
            ?? throw new InvalidOperationException($"No interaction handler found for extension '{extensionId}'.");

        var context = new CellInteractionContext
        {
            Region = region,
            InteractionType = interactionType,
            Payload = payload,
            OutputBlockId = outputBlockId,
            CellId = cellId,
            ExtensionId = extensionId,
            CancellationToken = CancellationToken.None,
            Variables = _scaffold.Variables,
            Notebook = _scaffold.NotebookOps,
            NotebookModel = _scaffold.Notebook
        };

        var response = await handler.OnCellInteractionAsync(context);

        if (response is not null)
        {
            var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
            if (cell is not null)
            {
                if (outputBlockId is not null)
                {
                    var existingIndex = cell.Outputs.FindIndex(o =>
                        o.Content.Contains($"data-output-id=\"{outputBlockId}\""));
                    if (existingIndex >= 0)
                        cell.Outputs[existingIndex] = new CellOutput("text/html", response);
                    else
                        cell.Outputs.Add(new CellOutput("text/html", response));
                }
                else
                {
                    // Replace all outputs (used by form-based cells like parameters)
                    cell.Outputs.Clear();
                    cell.Outputs.Add(new CellOutput("text/html", response));
                }

                OnOutputUpdated?.Invoke();
            }
        }

        return response;
    }

    // ── Editor intelligence ────────────────────────────────────────────

    public async Task<HoverResultDto?> GetHoverInfoAsync(Guid cellId, string code, int position)
    {
        if (_scaffold is null) return null;

        var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell?.Language is null) return null;

        var kernel = _scaffold.GetKernel(cell.Language);
        if (kernel is null) return null;

        await _scaffold.WarmUpKernelAsync(cell.Language);
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

        await _scaffold.WarmUpKernelAsync(cell.Language);
        var completions = await kernel.GetCompletionsAsync(code, position);
        return new CompletionsResultDto(
            completions.Select(c => new CompletionItemDto(
                c.DisplayText, c.InsertText, c.Kind, c.Description, c.SortText)).ToList());
    }

    // ── Layout & theme switching ───────────────────────────────────────

    public Task<string?> RenderActiveLayoutAsync() => Task.FromResult<string?>(null);

    public Task SwitchLayoutAsync(string layoutId)
    {
        if (_scaffold?.LayoutManager is null) return Task.CompletedTask;
        _scaffold.LayoutManager.SetActiveLayout(layoutId);
        if (_scaffold.LayoutManager.ActiveLayout is { } active && active is IExtension ext)
        {
            _scaffold.Notebook.ActiveLayout = new LayoutReference(ext.ExtensionId, active.LayoutId);
            _scaffold.Notebook.RequiresLegacyLayoutResolution = false;
        }
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

    public Task RefreshVariablesAsync()
    {
        // In Server mode, GetVariables() already reads the live store — nothing extra needed.
        OnVariablesChanged?.Invoke();
        return Task.CompletedTask;
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
        // Check directly-registered renderers first
        var renderer = _extensionHost?.GetRenderers()
            .FirstOrDefault(r => string.Equals(r.CellTypeId, cellType, StringComparison.OrdinalIgnoreCase));
        if (renderer is not null)
            return renderer.CollapsesInputOnExecute;

        // Also check renderers owned by ICellType registrations (e.g. HTML, Mermaid)
        var ct = _extensionHost?.GetCellTypes()
            .FirstOrDefault(t => string.Equals(t.CellTypeId, cellType, StringComparison.OrdinalIgnoreCase));
        return ct?.Renderer?.CollapsesInputOnExecute ?? false;
    }

    public bool IsCellTypeEditable(string cellType)
    {
        var ct = _extensionHost?.GetCellTypes()
            .FirstOrDefault(t => string.Equals(t.CellTypeId, cellType, StringComparison.OrdinalIgnoreCase));
        return ct?.IsEditable ?? true;
    }

    // ── Cell properties ──────────────────────────────────────────────

    public async Task<IReadOnlyList<PropertySectionResult>> GetCellPropertySectionsAsync(Guid cellId)
    {
        if (_scaffold is null || _extensionHost is null)
            return Array.Empty<PropertySectionResult>();

        var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is null)
            return Array.Empty<PropertySectionResult>();

        var context = new BlazorCellRenderContext(_scaffold, cell);
        var providers = _extensionHost.GetPropertyProviders()
            .Where(p => p.AppliesTo(cell, context))
            .OrderBy(p => p.Order)
            .ToList();

        var results = new List<PropertySectionResult>();
        foreach (var provider in providers)
        {
            try
            {
                var section = await provider.GetPropertiesSectionAsync(cell, context);
                results.Add(new PropertySectionResult(provider.ExtensionId, section));
            }
            catch
            {
                // Provider failure is non-fatal; skip the section.
            }
        }
        return results;
    }

    public async Task NotifyPropertyChangedAsync(Guid cellId, string providerExtensionId, string propertyName, object? value)
    {
        if (_scaffold is null || _extensionHost is null) return;

        var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is null) return;

        var provider = _extensionHost.GetPropertyProviders()
            .FirstOrDefault(p => string.Equals(p.ExtensionId, providerExtensionId, StringComparison.OrdinalIgnoreCase));
        if (provider is null) return;

        var context = new BlazorCellRenderContext(_scaffold, cell);
        await provider.OnPropertyChangedAsync(cell, propertyName, value, context);
        OnNotebookChanged?.Invoke();
    }

    public CellVisibilityState ResolveCellVisibility(Guid cellId)
    {
        if (_scaffold is null || _extensionHost is null) return CellVisibilityState.Visible;
        var layout = _scaffold.LayoutManager?.ActiveLayout;
        if (layout is null) return CellVisibilityState.Visible;
        var cell = _scaffold.Cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is null) return CellVisibilityState.Visible;
        var renderer = _extensionHost.GetRenderers()
            .FirstOrDefault(r => string.Equals(r.CellTypeId, cell.Type, StringComparison.OrdinalIgnoreCase));
        var hint = renderer?.DefaultVisibility ?? CellVisibilityHint.Content;
        return CellVisibilityResolver.Resolve(cell, hint, layout.LayoutId, layout.SupportedVisibilityStates);
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
            var enabledLayouts = _extensionHost.GetLayouts();
            var defaultLayout = enabledLayouts
                .FirstOrDefault(l => !l.RequiresCustomRenderer)
                ?? enabledLayouts.FirstOrDefault();
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

        if (_scaffold is not null)
        {
            _scaffold.OnCellExecuting += HandleScaffoldCellExecuting;
            _scaffold.OnCellExecuted += HandleScaffoldCellExecuted;
            _scaffold.OnCellOutputUpdated += HandleScaffoldCellOutputUpdated;
            _scaffold.InputRequester = RequestInputFromUIAsync;
        }

        if (_scaffold?.Variables is VariableStore vs)
            vs.OnVariablesChanged += HandleVariablesChanged;

        if (_scaffold?.SettingsManager is { } sm)
            sm.OnSettingsChanged += HandleSettingsChanged;
    }

    private void UnsubscribeFromEngineEvents()
    {
        if (_extensionHost is not null)
            _extensionHost.OnExtensionStatusChanged -= HandleExtensionStatusChanged;

        if (_scaffold is not null)
        {
            _scaffold.OnCellExecuting -= HandleScaffoldCellExecuting;
            _scaffold.OnCellExecuted -= HandleScaffoldCellExecuted;
            _scaffold.OnCellOutputUpdated -= HandleScaffoldCellOutputUpdated;
            _scaffold.InputRequester = null;
        }

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

    private void HandleScaffoldCellExecuting(Guid cellId)
        => OnCellExecuting?.Invoke(cellId);

    private void HandleScaffoldCellExecuted(Guid cellId)
    {
        OnCellExecutionCompleted?.Invoke(cellId);
        OnCellExecuted?.Invoke();
    }

    private void HandleScaffoldCellOutputUpdated(Guid cellId)
        => OnOutputUpdated?.Invoke();

    private async Task<string?> RequestInputFromUIAsync(
        Guid cellId,
        string prompt,
        bool isPassword,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_inputLock)
        {
            if (_inputTcs is not null)
                throw new InvalidOperationException("Another input request is already pending.");

            _inputTcs = tcs;
            _pendingInputRequest = new ServerInputRequest(cellId, prompt, isPassword);
        }

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<string?>)state!).TrySetResult(null),
            tcs);

        OnInputRequested?.Invoke();

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            if (ClearPendingInputRequest(tcs))
                OnInputRequested?.Invoke();
        }
    }

    private bool ClearPendingInputRequest(TaskCompletionSource<string?> tcs)
    {
        lock (_inputLock)
        {
            if (!ReferenceEquals(_inputTcs, tcs))
                return false;

            _inputTcs = null;
            _pendingInputRequest = null;
            return true;
        }
    }

    private async Task<bool> RequestConsentFromUIAsync(
        IReadOnlyList<ExtensionConsentInfo> extensions,
        CancellationToken cancellationToken)
    {
        _consentTcs = new TaskCompletionSource<bool>();
        _pendingConsentExtensions = extensions;

        using var registration = cancellationToken.Register(
            () => _consentTcs.TrySetResult(false));

        OnExtensionConsentRequested?.Invoke();

        return await _consentTcs.Task;
    }

    private void WireConsentHandler()
    {
        if (_extensionHost is not null)
            _extensionHost.ConsentHandler = RequestConsentFromUIAsync;
    }

    private async Task ScanAndRequestConsentAsync(NotebookModel notebook)
    {
        if (_extensionHost is null) return;
        var directives = ExtensionMagicCommand.ScanForExtensionDirectives(notebook);
        if (directives.Count > 0)
        {
            var approved = await _extensionHost.RequestExtensionConsentAsync(directives);
            if (approved)
            {
                foreach (var d in directives)
                    _extensionHost.ApprovePackage(d.PackageId);
            }
        }
    }

    private void WarmUpKernelsInBackground()
    {
        if (_scaffold is null) return;
        var languages = _scaffold.Cells
            .Where(c => c.Language is not null)
            .Select(c => c.Language!)
            .Append(_scaffold.DefaultKernelId ?? "csharp")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var scaffold = _scaffold;
        _ = Task.Run(async () =>
        {
            foreach (var lang in languages)
            {
                try { await scaffold.WarmUpKernelAsync(lang); }
                catch { /* warm-up failure is non-fatal */ }
            }
        });
    }

    private async Task DisposeCurrentAsync()
    {
        _executionCts?.Cancel();
        ResolveInputResult(null, cancelled: true);
        UnsubscribeFromEngineEvents();
        if (_scaffold is not null)
        {
            await _scaffold.DisposeAsync();
            _scaffold = null;
        }
        _executionCts?.Dispose();
        _executionCts = null;
        _extensionHost = null;
        _filePath = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeCurrentAsync();
    }
}
