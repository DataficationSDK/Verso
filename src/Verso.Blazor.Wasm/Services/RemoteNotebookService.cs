using System.Text.Json;
using Microsoft.JSInterop;
using Verso.Abstractions;
using Verso.Blazor.Shared.Models;
using Verso.Blazor.Shared.Services;
using Verso.Blazor.Shared.Resources;

namespace Verso.Blazor.Wasm.Services;

/// <summary>
/// Implements <see cref="INotebookService"/> for Blazor WebAssembly by relaying all
/// operations to the Verso.Host process through the VS Code postMessage ↔ JSON-RPC bridge.
/// Maintains a local state cache that is populated on open and updated from responses/notifications.
/// </summary>
public sealed class RemoteNotebookService : IIsolatedLayoutHost, IAsyncDisposable
{
    private readonly VsCodeBridge _bridge;
    private readonly IJSRuntime _js;

    // Cache of blob URLs already minted for (extensionId, layoutId, assetId). Survives across
    // re-renders so switching to a layout we've seen this session does not refetch bytes or
    // re-create blob URLs. Cleared on extension reload via HandleExtensionChangedAsync.
    private readonly Dictionary<(string ExtensionId, string LayoutId, string AssetId), string>
        _layoutAssetUrlCache = new();

    // ── Local cache ─────────────────────────────────────────────────────
    private bool _isLoaded;

    private string? _filePath;
    private string? _title;
    private string? _defaultKernelId;
    private List<KernelLanguageInfo> _registeredLanguages = new();
    private DateTimeOffset? _created;
    private DateTimeOffset? _modified;
    private string _formatVersion = "";
    private List<CellModel> _cells = new();
    private List<CellTypeInfo> _cellTypes = new();
    private List<ToolbarActionInfo> _toolbarActions = new();
    private List<LayoutInfo> _layouts = new();
    private List<ThemeInfo> _themes = new();
    private List<ExtensionInfo> _extensions = new();
    private List<InstalledExtensionDto> _installedExtensions = new();
    private List<string> _marketplaceSources = new();
    // Required extensions the host reported as failed-to-load, keyed by package id, so installed
    // rows can be flagged. The unavailable notification and the installed list can arrive in either
    // order, so this is applied both when building the list and when the notification lands.
    private Dictionary<string, string> _unavailableExtensionReasons = new(StringComparer.OrdinalIgnoreCase);
    private LayoutReference? _activeLayout;
    private string? _activeThemeId;
    private ThemeKind? _activeThemeKind;
    private bool _isDashboardLayout;
    private string? _activeLayoutRendererIsolation;
    private bool _activeLayoutSupportsPropertiesPanel;
    private LayoutCapabilities _layoutCapabilities = LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete
        | LayoutCapabilities.CellReorder | LayoutCapabilities.CellEdit | LayoutCapabilities.CellResize
        | LayoutCapabilities.CellExecute | LayoutCapabilities.MultiSelect;

    // ── Debounce for cell source updates ────────────────────────────────
    private readonly Dictionary<Guid, CancellationTokenSource> _debounceCts = new();
    private const int DebounceDelayMs = 250;

    // ── Coalesced whole-page output notifications ───────────────────────
    // A burst of Display() calls during Run All would otherwise raise OnOutputUpdated (and a whole-page
    // StateHasChanged) once per output. Cell outputs are updated synchronously as each notification
    // arrives; only the render notification is throttled to roughly one per frame, with a trailing raise
    // guaranteeing the final state renders.
    private bool _outputNotifyScheduled;
    private const int OutputNotifyCoalesceMs = 32;

    // ── Events ──────────────────────────────────────────────────────────
    public event Action? OnCellExecuted;
    public event Action<Guid>? OnCellExecuting;
    public event Action<Guid>? OnCellExecutionCompleted;
    public event Action? OnNotebookChanged;
    public event Action? OnLayoutChanged;
    public event Action<LayoutUpdatedEventArgs>? OnLayoutUpdated;

    public event Action<PanelUpdatedEventArgs>? OnPanelUpdated;
    public event Action? OnThemeChanged;
    public event Action? OnExtensionStatusChanged;
    public event Action? OnVariablesChanged;
    public event Action? OnSettingsChanged;
    public event Action? OnOutputUpdated;

    /// <summary>
    /// Raised for each <c>output/update</c> notification with the cell id and raw
    /// outputs payload so isolated-layout iframes can re-broadcast as
    /// <c>verso/cellOutputs</c> without re-fetching the cell list.
    /// </summary>
    public event Action<CellOutputUpdatedEventArgs>? OnCellOutputUpdated;

    /// <summary>
    /// Raised for each <c>layout/frameMessage</c> notification. Subscribers should
    /// filter by <see cref="LayoutFrameMessageEventArgs.FrameInstanceId"/> and
    /// forward the message to the matching iframe.
    /// </summary>
    public event Action<LayoutFrameMessageEventArgs>? OnLayoutFrameMessage;

    /// <summary>Raised when the host requests extension consent. The UI should show the consent dialog.</summary>
    public event Action? OnExtensionConsentRequested;

    /// <summary>Raised when the supervisor begins a kernel restart. The UI should show a "restarting" status banner.</summary>
    public event Action<string?>? OnKernelRestarting;

    /// <summary>Raised when the supervisor finishes a kernel restart. The UI should clear execution badges and update status.</summary>
    public event Action<string?>? OnKernelRestarted;

    /// <summary>Raised when the notebook references a layout that isn't registered yet. The argument is the missing layout id.</summary>
    public event Action<string>? OnLayoutMissing;

    /// <summary>Raised when required extensions a notebook declares could not be loaded on open.
    /// The notebook still opens; the UI should show a non-fatal notice.</summary>
    public event Action<IReadOnlyList<UnavailableExtensionInfo>>? OnRequiredExtensionsUnavailable;

    /// <summary>Raised when <see cref="IsDirty"/> changes.</summary>
    public event Action? OnDirtyStateChanged;

    /// <summary>Raised when the host process dies (Disconnected, from the extension's
    /// <c>host/exited</c> message) or an in-process kernel restart fails (Faulted, from the
    /// host's <c>kernel/faulted</c> notification).</summary>
    public event Action<KernelHealthChangedEventArgs>? OnKernelHealthChanged;

    /// <summary>Raised when the extension asks the UI to open the notebook diff view
    /// (command palette or editor-title button), via the <c>diff/requested</c> notification.</summary>
    public event Action<string?>? OnDiffRequested;

    // ── Extension consent state ────────────────────────────────────────
    private string? _pendingConsentRequestId;
    private IReadOnlyList<ExtensionConsentInfo>? _pendingConsentExtensions;

    /// <summary>The extensions awaiting consent, if any.</summary>
    public IReadOnlyList<ExtensionConsentInfo>? PendingConsentExtensions => _pendingConsentExtensions;

    /// <summary>Called by the UI to resolve the pending consent request.</summary>
    public async Task ResolveConsentResultAsync(bool approved)
    {
        if (_pendingConsentRequestId is null) return;
        var requestId = _pendingConsentRequestId;
        _pendingConsentRequestId = null;
        _pendingConsentExtensions = null;

        await _bridge.RequestVoidAsync("extension/consentResponse",
            new { requestId, approved });
    }

    public RemoteNotebookService(VsCodeBridge bridge, IJSRuntime js)
    {
        _bridge = bridge;
        _js = js;
        _bridge.OnNotification += HandleNotification;
    }

    // ── State properties ────────────────────────────────────────────────

    public bool IsLoaded => _isLoaded;
    public bool IsEmbedded => true;
    public string? FilePath => _filePath;

    // Local view of unsaved changes: set by this service's own mutations (the same set the
    // bridge uses to mark the VS Code document dirty) and cleared when the extension reports
    // the document was saved, so a Cmd/Ctrl+S outside the webview clears it too.
    private bool _isDirty;
    public bool IsDirty => _isDirty;

    private void SetDirty(bool value)
    {
        if (_isDirty == value) return;
        _isDirty = value;
        OnDirtyStateChanged?.Invoke();
    }

    // ── Notebook metadata ───────────────────────────────────────────────

    public string? Title
    {
        get => _title;
        set => _title = value;
    }

    public string? DefaultKernelId
    {
        get => _defaultKernelId;
        set
        {
            if (string.Equals(_defaultKernelId, value, StringComparison.OrdinalIgnoreCase)) return;
            _defaultKernelId = value;
            if (_isLoaded && value is not null)
            {
                _ = _bridge.RequestVoidAsync("notebook/setDefaultKernel", new { kernelId = value });
                SetDirty(true);
            }
        }
    }

    public IReadOnlyList<KernelLanguageInfo> RegisteredLanguages => _registeredLanguages;
    public DateTimeOffset? Created => _created;
    public DateTimeOffset? Modified => _modified;
    public string FormatVersion => _formatVersion;

    // ── Cells ───────────────────────────────────────────────────────────

    public IReadOnlyList<CellModel> Cells => _cells;

    // ── Layout & theme ──────────────────────────────────────────────────

    public bool IsDashboardLayout => _isDashboardLayout;
    public ThemeKind? ActiveThemeKind => _activeThemeKind;
    public ThemeData? ActiveThemeData { get; private set; }

    /// <summary>
    /// Resolved theme bundle for iframe-isolated layout renderers. Returns <c>null</c> until
    /// <see cref="ActiveThemeData"/> has been populated. The bundle's token keys match the
    /// host's documented CSS custom-property palette so iframe stylesheets can reference
    /// the same variable names as the host page.
    /// </summary>
    public LayoutThemeBundle? CurrentTheme => LayoutThemeBundleBuilder.Build(_activeThemeKind, ActiveThemeData);

    /// <summary>
    /// Qualified identity of the currently active layout, or <c>null</c> when no notebook
    /// is loaded. The pair-shape is the canonical client-side identifier from v1.0 onward.
    /// </summary>
    public LayoutReference? ActiveLayout => _activeLayout;

    /// <summary>
    /// Bare <c>LayoutId</c> of the active layout. Retained as a compatibility forwarder for
    /// callers that have not yet migrated to <see cref="ActiveLayout"/>; scheduled for
    /// removal in v2.0.
    /// </summary>
    public string? ActiveLayoutId => _activeLayout?.LayoutId;
    public string ActiveLayoutKey => _activeLayout is { } layout
        ? $"{layout.ExtensionId}:{layout.LayoutId}"
        : "verso.layout:none";
    public LayoutCapabilities LayoutCapabilities => _layoutCapabilities;
    public bool ActiveLayoutSupportsPropertiesPanel => _activeLayoutSupportsPropertiesPanel;
    public string? ActiveLayoutRendererIsolation => _activeLayoutRendererIsolation;
    public string? ActiveThemeId => _activeThemeId;

    // ── Extension data ──────────────────────────────────────────────────

    public IReadOnlyList<CellTypeInfo> AvailableCellTypes => _cellTypes;
    public IReadOnlyList<LayoutInfo> AvailableLayouts => _layouts;
    public IReadOnlyList<ThemeInfo> AvailableThemes => _themes;
    public IReadOnlyList<ExtensionInfo> Extensions => _extensions;
    public IReadOnlyList<InstalledExtensionDto> InstalledExtensions => _installedExtensions;
    public IReadOnlyList<string> MarketplaceSources => _marketplaceSources;

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
        SetDirty(false);

        // Fetch supplementary data
        await RefreshExtensionDataAsync();
        await RefreshThemeDataAsync();

        OnNotebookChanged?.Invoke();
    }

    public Task<string?> GetSerializedContentAsync()
    {
        // WASM mode doesn't support client-side serialization; the host handles saving.
        return Task.FromResult<string?>(null);
    }

    // ── Notebook diff ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<DiffSourceInfo>> GetDiffSourcesAsync()
    {
        // Intercepted by the extension bridge; never reaches the host process.
        var response = await _bridge.RequestAsync<DiffSourcesResponse>("diff/sources", new { });
        return (response.Sources ?? new List<DiffSourceItem>())
            .Select(s => new DiffSourceInfo(
                s.Id ?? "",
                SourceLabel(s),
                s.Kind ?? "",
                s.Available,
                SourceDescription(s)))
            .ToList();
    }

    /// <summary>
    /// Names one of the baselines a notebook can be compared against.
    /// </summary>
    /// <remarks>
    /// The name the editor sent is a fallback, not the answer. It wrote it in the language
    /// its workbench is set to, and this panel is written in the language the notebook
    /// interface is set to. Anything the editor offers beyond the four built-in sources is
    /// used as it arrived, since nothing here knows what it is.
    /// </remarks>
    private static string SourceLabel(DiffSourceItem source)
        => DiffSources.NameOf(source.Id) ?? source.Label ?? source.Id ?? "";

    /// <summary>
    /// Why a baseline cannot be used, for the two reasons the panel knows how to explain.
    /// </summary>
    private static string? SourceDescription(DiffSourceItem source)
        => source.Available || source.Description is null
            ? source.Description
            : DiffSources.UnavailableReason(source.Id) ?? source.Description;

    public async Task<NotebookDiffResult?> ComputeDiffAsync(string sourceId, string? explicitInput = null)
    {
        // explicitInput is unused here: the extension resolves gitRef/file inputs with
        // native pickers inside diff/baseline, so this host never prompts locally.
        var baseline = await _bridge.RequestAsync<DiffBaselineResponse>(
            "diff/baseline", new { sourceId });
        if (baseline.Cancelled == true || baseline.Content is null)
        {
            return null;
        }

        // Forwarded to the host process (notebookId is injected by the bridge), which
        // parses the baseline and diffs it against the live in-memory notebook.
        return await _bridge.RequestAsync<NotebookDiffResult>("notebook/diff", new
        {
            baselineContent = baseline.Content,
            baselineFilePath = baseline.FilePath,
            baselineLabel = BaselineLabel(baseline),
        });
    }

    /// <summary>
    /// Names the baseline a comparison ran against, for the heading over the change list.
    /// </summary>
    /// <remarks>
    /// The editor picked the baseline and says which one it is; the words for it are
    /// chosen here, in the notebook interface language. What travels across is only what
    /// no language can change: the git ref that was typed, and the name of a file that was
    /// chosen.
    /// </remarks>
    private static string BaselineLabel(DiffBaselineResponse baseline)
        => DiffSources.ResolvedName(baseline.LabelKind, baseline.LabelArg);

    public async Task SaveAsync(string filePath)
    {
        // Get the serialized notebook content from the host
        var result = await _bridge.RequestAsync<SaveResponse>("notebook/save", null);

        // Route the content through the bridge for the VS Code extension to write to disk
        if (result.Content is not null)
        {
            await _bridge.RequestVoidAsync("extension/writeFile", new { content = result.Content, filePath });
            SetDirty(false);
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
        SetDirty(true);
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
        SetDirty(true);
        OnNotebookChanged?.Invoke();
        return cell;
    }

    public async Task<bool> RemoveCellAsync(Guid cellId)
    {
        await _bridge.RequestVoidAsync("cell/remove", new { cellId = cellId.ToString() });
        var removed = _cells.RemoveAll(c => c.Id == cellId) > 0;
        if (removed)
        {
            SetDirty(true);
            OnNotebookChanged?.Invoke();
        }
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

        SetDirty(true);
        OnNotebookChanged?.Invoke();
    }

    public async Task UpdateCellSourceAsync(Guid cellId, string source)
    {
        // Update local cache immediately for responsive UI
        var cell = _cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is not null)
            cell.Source = source;

        SetDirty(true);

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

    /// <summary>
    /// Cancel any pending debounced source update for <paramref name="cellId"/> and send
    /// the current local source to the host immediately.  Called before execution so the
    /// host always has the latest source.
    /// </summary>
    private async Task FlushPendingSourceUpdateAsync(Guid cellId)
    {
        // A debounce entry exists only when the user edited this cell's source and that
        // edit has not yet reached the host (see UpdateCellSourceAsync). With no pending
        // entry the host already holds the current source, so there is nothing to flush,
        // and re-sending it would surface as a spurious cell/updateSource mutation (for
        // example on the auto-execute-on-open of a non-editable cell), falsely marking
        // the document edited.
        if (!_debounceCts.TryGetValue(cellId, out var cts))
            return;

        cts.Cancel();
        cts.Dispose();
        _debounceCts.Remove(cellId);

        var cell = _cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is not null)
        {
            await _bridge.RequestVoidAsync("cell/updateSource",
                new { cellId = cellId.ToString(), source = cell.Source });
        }
    }

    public async Task ChangeCellTypeAsync(Guid cellId, string newType)
    {
        await _bridge.RequestVoidAsync("cell/changeType", new { cellId = cellId.ToString(), type = newType });
        // Re-fetch cell list after type change since the host may rebuild the cell
        await RefreshCellListAsync();
        SetDirty(true);
        OnNotebookChanged?.Invoke();
    }

    public async Task ChangeCellLanguageAsync(Guid cellId, string newLanguage)
    {
        await _bridge.RequestVoidAsync("cell/changeLanguage", new { cellId = cellId.ToString(), language = newLanguage });
        await RefreshCellListAsync();
        SetDirty(true);
        OnNotebookChanged?.Invoke();
    }

    public async Task ClearAllOutputsAsync()
    {
        await _bridge.RequestVoidAsync("output/clearAll", null);

        foreach (var cell in _cells)
            cell.Outputs.Clear();

        SetDirty(true);
        OnCellExecuted?.Invoke();
    }

    // ── Execution ───────────────────────────────────────────────────────

    public async Task<ExecutionResultDto> ExecuteCellAsync(Guid cellId)
    {
        // Flush any debounced source update so the host has the latest source
        await FlushPendingSourceUpdateAsync(cellId);

        var result = await _bridge.RequestAsync<ExecutionResponse>(
            "execution/run",
            new { cellId = cellId.ToString() });

        // Update local cell outputs and execution metadata.
        // The scaffold runs in the host process, so its metadata stamping
        // doesn't reach the WASM-side cell cache — stamp it here from the RPC response.
        var cell = _cells.FirstOrDefault(c => c.Id == cellId);
        if (cell is not null)
        {
            if (result.Outputs is not null)
            {
                cell.Outputs.Clear();
                foreach (var o in result.Outputs)
                    cell.Outputs.Add(MapOutputFromDto(o));
            }
            cell.ExecutionCount = result.ExecutionCount;
            cell.LastElapsed = TimeSpan.FromMilliseconds(result.ElapsedMs);
            cell.LastStatus = result.Status;
        }

        // Execution mutates outputs, which are part of the saved file — but only for cell types that
        // persist their outputs. The host reports this per execution so an auto-render on open of a
        // transient-output cell (e.g. the parameters form) does not mark the notebook edited.
        if (result.Dirty)
            SetDirty(true);

        // Refresh variables directly after execution instead of relying
        // solely on the fire-and-forget notification handler.
        await RefreshVariablesSafeAsync();

        // OnCellExecuted is fired per cell by HandleCellExecutionState when the
        // host sends the "completed"/"failed"/"cancelled" notification. No fire here.

        return new ExecutionResultDto(
            cellId,
            result.Status ?? "completed",
            result.ExecutionCount,
            TimeSpan.FromMilliseconds(result.ElapsedMs));
    }

    public async Task<IReadOnlyList<ExecutionResultDto>> ExecuteAllAsync()
    {
        // Flush all pending source updates before running
        foreach (var cell in _cells)
            await FlushPendingSourceUpdateAsync(cell.Id);

        var response = await _bridge.RequestAsync<ExecutionRunAllResponse>(
            "execution/runAll", null);

        // Dirty only if at least one executed cell persists its outputs.
        if (response.Results?.Any(r => r.Dirty) == true)
            SetDirty(true);
        await RefreshCellListAsync();
        StampExecutionMetadata(response.Results);
        await RefreshVariablesSafeAsync();

        // Per-cell OnCellExecuted notifications arrive via HandleCellExecutionState
        // as the host streams them during the batch. No single trailing fire here.

        if (response.Results is null or { Count: 0 })
            return Array.Empty<ExecutionResultDto>();

        var dtos = new List<ExecutionResultDto>(response.Results.Count);
        foreach (var r in response.Results)
        {
            if (!Guid.TryParse(r.CellId, out var cellId))
                continue;

            dtos.Add(new ExecutionResultDto(
                cellId,
                r.Status ?? "completed",
                r.ExecutionCount,
                TimeSpan.FromMilliseconds(r.ElapsedMs)));
        }

        return dtos;
    }

    /// <summary>
    /// Copies each result's execution metadata onto the cached cell. Needed because
    /// RefreshCellListAsync re-maps the cells and MapCellFromDto does not carry these fields.
    /// </summary>
    /// <remarks>
    /// Called immediately after that refresh, with nothing awaited in between, so no render sees a
    /// cell whose new outputs have arrived and whose count has not. A cell shown in that state is
    /// wrong on its own (the status line reports the previous run), and it also means the outputs
    /// are rendered once and then again when the count lands, which an output that is a live
    /// element rather than markup pays for by being rebuilt.
    /// </remarks>
    private void StampExecutionMetadata(List<ExecutionRunAllResultDto>? results)
    {
        if (results is null) return;

        foreach (var r in results)
        {
            if (!Guid.TryParse(r.CellId, out var cellId))
                continue;

            var cached = _cells.FirstOrDefault(c => c.Id == cellId);
            if (cached is null)
                continue;

            cached.ExecutionCount = r.ExecutionCount;
            cached.LastElapsed = TimeSpan.FromMilliseconds(r.ElapsedMs);
            cached.LastStatus = r.Status;
        }
    }

    public async Task CancelCellAsync(Guid cellId)
    {
        // Fire-and-forget conceptually — the host routes execution/cancel out-of-band
        // so the response returns promptly, but the in-flight execution/run RPC is
        // what unblocks once the kernel observes the cancellation token.
        await _bridge.RequestVoidAsync("execution/cancel", new { cellId = cellId.ToString() });
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
        SetDirty(true);
        await RefreshCellListAsync();
        await RefreshVariablesSafeAsync();
        OnCellExecuted?.Invoke();
    }

    // ── Cell interaction ────────────────────────────────────────────────

    public async Task<string?> HandleCellInteractionAsync(
        Guid cellId, string extensionId, string interactionType,
        string payload, string? outputBlockId, CellRegion region)
    {
        var result = await _bridge.RequestAsync<CellInteractResponse>("cell/interact", new
        {
            cellId = cellId.ToString(),
            extensionId,
            interactionType,
            payload,
            outputBlockId,
            region = region.ToString()
        });

        if (result?.Response is not null)
        {
            var cell = _cells.FirstOrDefault(c => c.Id == cellId);
            if (cell is not null)
            {
                if (outputBlockId is not null)
                {
                    var existingIndex = cell.Outputs.FindIndex(o =>
                        o.Content.Contains($"data-output-id=\"{outputBlockId}\""));
                    if (existingIndex >= 0)
                        cell.Outputs[existingIndex] = new CellOutput("text/html", result.Response);
                    else
                        cell.Outputs.Add(new CellOutput("text/html", result.Response));
                }
                else
                {
                    // Replace all outputs (used by form-based cells like parameters)
                    cell.Outputs.Clear();
                    cell.Outputs.Add(new CellOutput("text/html", result.Response));
                }

                OnOutputUpdated?.Invoke();
            }
        }

        // A parameter-definition edit (add/update/remove/toggle-required) changes persisted state;
        // the host reports it so the notebook is marked edited even though execution never ran.
        if (result?.Dirty == true)
            SetDirty(true);

        return result?.Response;
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

    public Task SwitchLayoutAsync(string layoutId)
        => SwitchLayoutAsync(new LayoutReference(string.Empty, layoutId));

    public async Task SwitchLayoutAsync(LayoutReference reference)
    {
        var payload = string.IsNullOrEmpty(reference.ExtensionId)
            ? (object)new { layoutId = reference.LayoutId }
            : new { layoutId = reference.LayoutId, extensionId = reference.ExtensionId };

        await _bridge.RequestVoidAsync("layout/switch", payload);

        ApplyActiveLayout(reference);
    }

    /// <summary>
    /// Update the cached active-layout state from a switch that has already taken
    /// effect on the host, then raise <see cref="OnLayoutChanged"/> so the page
    /// swaps to the new layout. Shared by the client-initiated path
    /// (<see cref="SwitchLayoutAsync(LayoutReference)"/>) and the host-initiated
    /// path (a <c>layout/activeChanged</c> notification, e.g. an external switch
    /// driven by the VS Code chat participant).
    /// </summary>
    private void ApplyActiveLayout(LayoutReference reference)
    {
        var match = _layouts.FirstOrDefault(l =>
            string.Equals(l.LayoutId, reference.LayoutId, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(reference.ExtensionId) ||
             string.Equals(l.ExtensionId, reference.ExtensionId, StringComparison.OrdinalIgnoreCase)));

        _activeLayout = match is not null && !string.IsNullOrEmpty(match.ExtensionId)
            ? new LayoutReference(match.ExtensionId, match.LayoutId)
            : reference;
        _isDashboardLayout = match?.RequiresCustomRenderer ?? false;
        _layoutCapabilities = match?.Capabilities ?? _layoutCapabilities;
        _activeLayoutSupportsPropertiesPanel = match?.SupportsPropertiesPanel ?? false;
        _activeLayoutRendererIsolation = match?.RendererIsolation;
        OnLayoutChanged?.Invoke();
    }

    private IReadOnlySet<Guid> _collapsedSections = new HashSet<Guid>();

    /// <summary>
    /// The heading cells whose sections are collapsed. The page holds the source of truth and hands
    /// its live set here; assigning re-renders the active custom layout (the render request carries
    /// the set to the host so the layout folds the cells under a collapsed heading). The built-in
    /// cell list folds client-side and does not depend on this.
    /// </summary>
    public IReadOnlySet<Guid> CollapsedSections
    {
        get => _collapsedSections;
        set
        {
            _collapsedSections = value ?? new HashSet<Guid>();
            if (_isDashboardLayout && _activeLayout is { } layout)
            {
                OnLayoutUpdated?.Invoke(new LayoutUpdatedEventArgs(
                    layout.ExtensionId, layout.LayoutId, string.Empty, "full", null));
            }
        }
    }

    public async Task<string?> RenderActiveLayoutAsync()
    {
        var collapsed = _collapsedSections.Select(id => id.ToString()).ToList();
        var result = await _bridge.RequestAsync<LayoutRenderResponse>(
            "layout/render", new { collapsedSections = collapsed });
        return result?.Html;
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

    // ── Extension marketplace ───────────────────────────────────────────

    public bool IsMarketplaceSupported => true;

    public async Task<IReadOnlyList<PackageSearchResultDto>> SearchExtensionsAsync(
        string query, int skip, int take, bool includePrerelease, CancellationToken ct)
    {
        var response = await _bridge.RequestAsync<ExtensionSearchResponse>(
            "extension/search",
            new { query, skip, take, includePrerelease });

        return response.Packages?.Select(p => new PackageSearchResultDto(
            p.Id ?? "",
            p.Version,
            p.Description,
            p.Authors,
            p.DownloadCount,
            p.IconUrl,
            p.ProjectUrl,
            p.IsInstalled)).ToList()
            ?? (IReadOnlyList<PackageSearchResultDto>)Array.Empty<PackageSearchResultDto>();
    }

    public async Task<IReadOnlyList<string>> GetExtensionVersionsAsync(
        string packageId, bool includePrerelease, CancellationToken ct)
    {
        var response = await _bridge.RequestAsync<ExtensionVersionsResponse>(
            "extension/versions",
            new { packageId, includePrerelease });

        return response.Versions ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public async Task<PackageInstallResultDto> InstallExtensionAsync(
        string packageId, string? version, CancellationToken ct)
    {
        var response = await _bridge.RequestAsync<ExtensionInstallResponse>(
            "extension/install",
            new { packageId, version });

        if (response.Success)
        {
            ClearUnavailableReason(response.PackageId ?? packageId);
            await RefreshExtensionDataAsync();
            OnExtensionStatusChanged?.Invoke();
        }

        return new PackageInstallResultDto(
            response.Success, response.ResolvedVersion, response.ErrorMessage, response.ExtensionsRegistered);
    }

    public async Task UninstallExtensionAsync(string packageId)
    {
        await _bridge.RequestVoidAsync("extension/uninstall", new { packageId });
        ClearUnavailableReason(packageId);
        await RefreshExtensionDataAsync();
        OnExtensionStatusChanged?.Invoke();
    }

    // Drops the stale unavailable mark for a package once it is installed, sideloaded, or removed,
    // so the warning icon does not linger after the package becomes available (or stops being
    // required). The reasons dictionary is otherwise only repopulated on notebook open.
    private void ClearUnavailableReason(string? packageId)
    {
        if (!string.IsNullOrEmpty(packageId))
            _unavailableExtensionReasons.Remove(packageId);
    }

    public LocalExtensionPickMode LocalExtensionPickMode => LocalExtensionPickMode.NativeBrowse;

    public Task<PackageInstallResultDto> InstallLocalExtensionAsync(string fileName, Stream content, CancellationToken ct)
        => throw new NotSupportedException("The embedded host browses for files natively; use BrowseAndInstallLocalExtensionAsync.");

    public async Task<PackageInstallResultDto> BrowseAndInstallLocalExtensionAsync(CancellationToken ct)
    {
        // The bridge intercepts this locally and shows VS Code's native open dialog rather than
        // forwarding to the host, so the chosen path is a real path on the host machine.
        var picked = await _bridge.RequestAsync<BrowseFileResponse>("extension/browseLocalFile", null);
        if (string.IsNullOrEmpty(picked?.Path))
            return new PackageInstallResultDto(false, null, null, 0); // user cancelled

        var response = await _bridge.RequestAsync<ExtensionInstallResponse>(
            "extension/installLocal", new { path = picked.Path });

        if (response.Success)
        {
            ClearUnavailableReason(response.PackageId);
            await RefreshExtensionDataAsync();
            OnExtensionStatusChanged?.Invoke();
        }

        return new PackageInstallResultDto(
            response.Success, response.ResolvedVersion, response.ErrorMessage, response.ExtensionsRegistered);
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

    public Task LayoutInteractAsync(
        string extensionId,
        string layoutId,
        string interactionType,
        string payload,
        string? frameInstanceId = null,
        string? targetId = null)
    {
        return _bridge.RequestVoidAsync("layout/interact", new
        {
            extensionId,
            layoutId,
            frameInstanceId = frameInstanceId ?? string.Empty,
            interactionType,
            payload,
            targetId
        });
    }

    public async Task<LayoutRendererPackageDto?> GetLayoutRendererPackageAsync(
        string extensionId,
        string layoutId)
    {
        var response = await _bridge.RequestAsync<LayoutRendererPackageResponse?>(
            "layout/getRendererPackage",
            new { extensionId, layoutId });

        if (response is null) return null;

        var decoded = new Dictionary<string, byte[]>(
            response.Files?.Count ?? 0,
            StringComparer.Ordinal);
        if (response.Files is not null)
        {
            foreach (var (path, base64) in response.Files)
                decoded[path] = Convert.FromBase64String(base64);
        }

        return new LayoutRendererPackageDto(
            response.EntryPoint,
            decoded,
            response.ContentSecurityPolicy,
            response.RendererProtocolVersion);
    }

    public async Task<IReadOnlyList<LayoutStaticAssetDescriptor>> GetLayoutStaticAssetsAsync(
        string extensionId,
        string layoutId)
    {
        // Capabilities are fixed for the HTML hosts in v1.x. When other content types
        // or render formats arrive (Skia, native), surface this through a per-host
        // capabilities object instead of hardcoding here.
        var response = await _bridge.RequestAsync<LayoutStaticAssetsResponse?>(
            "layout/getStaticAssets",
            new
            {
                extensionId,
                layoutId,
                supportedAssetContentTypes = new[] { "text/css", "text/javascript", "application/javascript" },
                supportedRenderFormats = new[] { "text/html" }
            });

        if (response?.Assets is null || response.Assets.Count == 0)
            return Array.Empty<LayoutStaticAssetDescriptor>();

        var descriptors = new List<LayoutStaticAssetDescriptor>(response.Assets.Count);
        foreach (var asset in response.Assets)
        {
            var key = (extensionId, layoutId, asset.AssetId);
            if (!_layoutAssetUrlCache.TryGetValue(key, out var url))
            {
                // The interop helper materializes the base64 into a Blob and returns a
                // stable blob: URL kept alive by the layout's release-on-switch hook.
                url = await _js.InvokeAsync<string>(
                    "versoLayoutAssets.registerAsset",
                    extensionId, layoutId, asset.AssetId, asset.ContentType, asset.Content);
                _layoutAssetUrlCache[key] = url;
            }
            descriptors.Add(new LayoutStaticAssetDescriptor(asset.AssetId, asset.ContentType, url)
            {
                LoadHints = DecodeLoadHints(asset.LoadHints),
                ContentSecurityPolicy = asset.ContentSecurityPolicy,
            });
        }

        return descriptors;
    }

    private static LayoutStaticAssetLoadHints? DecodeLoadHints(LayoutStaticAssetLoadHintsItem? wire)
    {
        if (wire is null) return null;
        var moduleKind = string.Equals(wire.ModuleKind, "module", StringComparison.Ordinal)
            ? LayoutScriptModuleKind.Module
            : LayoutScriptModuleKind.Classic;
        var loadMode = wire.LoadMode switch
        {
            "async" => LayoutScriptLoadMode.Async,
            "blocking" => LayoutScriptLoadMode.Blocking,
            _ => LayoutScriptLoadMode.Defer,
        };
        var placement = string.Equals(wire.Placement, "beforeLayoutHtml", StringComparison.Ordinal)
            ? LayoutScriptPlacement.BeforeLayoutHtml
            : LayoutScriptPlacement.AfterLayoutHtml;
        return new LayoutStaticAssetLoadHints(moduleKind, loadMode, placement);
    }

    /// <summary>
    /// Releases every cached blob URL scoped to a given layout. Called by the host
    /// component when the layout is switched away so blobs are not retained beyond
    /// their useful lifetime.
    /// </summary>
    public async Task ReleaseLayoutStaticAssetsAsync(string extensionId, string layoutId)
    {
        var keysToRemove = _layoutAssetUrlCache.Keys
            .Where(k => k.ExtensionId == extensionId && k.LayoutId == layoutId)
            .ToList();
        foreach (var key in keysToRemove)
            _layoutAssetUrlCache.Remove(key);

        try
        {
            await _js.InvokeVoidAsync("versoLayoutAssets.releaseLayout", extensionId, layoutId);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }

    public async Task<string> AllocateLayoutFrameInstanceAsync(string extensionId, string layoutId)
    {
        var response = await _bridge.RequestAsync<AllocateFrameInstanceResponse>(
            "layout/allocateFrameInstance",
            new { extensionId, layoutId });

        return response?.FrameInstanceId
            ?? throw new InvalidOperationException(
                "layout/allocateFrameInstance returned no frame instance id.");
    }

    public async Task<IDictionary<string, JsonElement>?> LayoutRendererMountedAsync(
        string extensionId,
        string layoutId,
        string frameInstanceId)
    {
        var response = await _bridge.RequestAsync<LayoutRendererMountedResponse>(
            "layout/rendererMounted",
            new { extensionId, layoutId, frameInstanceId });

        return response?.Extension;
    }

    public Task LayoutRendererUnmountedAsync(
        string extensionId,
        string layoutId,
        string frameInstanceId)
        => _bridge.RequestVoidAsync(
            "layout/rendererUnmounted",
            new { extensionId, layoutId, frameInstanceId });

    public Task LogExtensionAsync(
        string extensionId,
        string layoutId,
        string frameInstanceId,
        string level,
        string message)
        => _bridge.RequestVoidAsync(
            "log/extension",
            new { extensionId, layoutId, frameInstanceId, level, message });

    [Obsolete("Forwards to LayoutInteractAsync. Scheduled for removal in v2.0.")]
    public Task UpdateCellPositionAsync(Guid cellId, int row, int col, int colSpan, int rowSpan)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            cellId = cellId.ToString(),
            row,
            col,
            width = colSpan,
            height = rowSpan
        });

        return LayoutInteractAsync("verso.layout.dashboard", "dashboard", "updateCellPosition", payload);
    }

    // ── Cell type helpers ───────────────────────────────────────────────

    public bool ShouldCollapseInput(string cellType)
    {
        // In WASM, check if this cell type is a rendering cell (markdown, html, mermaid, etc.)
        return string.Equals(cellType, "markdown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cellType, "html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cellType, "mermaid", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsCellTypeEditable(string cellType)
    {
        // Consult the cell-type registry (fetched via notebook/getCellTypes and cached before the
        // notebook renders) so any non-editable cell type — first- or third-party — is honored,
        // instead of hardcoding a single built-in identifier.
        var ct = _cellTypes.FirstOrDefault(t => string.Equals(t.Id, cellType, StringComparison.OrdinalIgnoreCase));
        return ct?.IsEditable ?? true;
    }

    // ── Cell properties ──────────────────────────────────────────────

    public async Task<IReadOnlyList<PropertySectionResult>> GetCellPropertySectionsAsync(Guid cellId)
    {
        var response = await _bridge.RequestAsync<PropertiesGetSectionsResponse>(
            "properties/getSections",
            new { cellId = cellId.ToString() });

        if (response.Sections is null or { Count: 0 })
            return Array.Empty<PropertySectionResult>();

        var results = new List<PropertySectionResult>(response.Sections.Count);
        foreach (var r in response.Sections)
        {
            var fields = r.Section?.Fields?.Select(f => new PropertyField(
                Name: f.Name,
                DisplayName: f.DisplayName,
                FieldType: Enum.TryParse<PropertyFieldType>(f.FieldType, true, out var ft)
                    ? ft : PropertyFieldType.Text,
                CurrentValue: f.CurrentValue,
                Description: f.Description,
                Options: f.Options?.Select(o => new PropertyFieldOption(o.Value, o.DisplayName)).ToList(),
                IsReadOnly: f.IsReadOnly
            )).ToList() ?? new();

            var section = new PropertySection(
                r.Section?.Title ?? "",
                r.Section?.Description,
                fields);
            results.Add(new PropertySectionResult(r.ProviderExtensionId, section));
        }

        return results;
    }

    public async Task NotifyPropertyChangedAsync(Guid cellId, string providerExtensionId, string propertyName, object? value)
    {
        // Tags and MultiSelect pass IEnumerable<string>; serialize as
        // comma-separated so the round-trip through the string-typed JSON-RPC
        // protocol matches what PropertyFieldComponent.OnParametersSet parses.
        var serializedValue = value is IEnumerable<string> list
            ? string.Join(",", list)
            : value?.ToString();

        await _bridge.RequestVoidAsync(
            "properties/updateProperty",
            new
            {
                cellId = cellId.ToString(),
                providerExtensionId,
                propertyName,
                value = serializedValue
            });

        // Refresh local cell cache so metadata-dependent rendering (e.g. visibility) reflects the change
        await RefreshCellListAsync();
        SetDirty(true);
        OnNotebookChanged?.Invoke();
    }

    // ── Panels ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<NotebookPanelInfo>> GetPanelsAsync(Guid? selectedCellId)
    {
        var panels = new List<NotebookPanelInfo>(
            HostPanels.Available(
                ActiveLayoutSupportsPropertiesPanel,
                HostPanels.HasViewChoices(AvailableLayouts.Count, AvailableThemes.Count, IsEmbedded)));

        try
        {
            var response = await _bridge.RequestAsync<PanelListResponse>(
                "panel/list",
                new { selectedCellId = selectedCellId?.ToString() });

            foreach (var p in response.Panels ?? new List<PanelInfoResponse>())
            {
                panels.Add(new NotebookPanelInfo(
                    p.PanelId,
                    p.ExtensionId,
                    p.DisplayName,
                    p.IconName,
                    p.IconMarkup,
                    p.Order,
                    IsHostPanel: false,
                    Description: p.Description));
            }
        }
        catch
        {
            // An older host that does not know panel/list still gets its own panels.
        }

        return panels.OrderBy(p => p.Order).ThenBy(p => p.DisplayName, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<RenderResult>> RenderPanelAsync(
        string extensionId, string panelId, Guid? selectedCellId)
    {
        try
        {
            var response = await _bridge.RequestAsync<PanelRenderResponse>(
                "panel/render",
                new { extensionId, panelId, selectedCellId = selectedCellId?.ToString() });

            if (response.Representations is null or { Count: 0 })
                return Array.Empty<RenderResult>();

            return response.Representations
                .Select(r => new RenderResult(r.MimeType, r.Content))
                .ToList();
        }
        catch
        {
            return Array.Empty<RenderResult>();
        }
    }

    public Task PanelInteractAsync(
        string extensionId,
        string panelId,
        string interactionType,
        string payload,
        string? targetId = null,
        Guid? selectedCellId = null)
        => _bridge.RequestVoidAsync(
            "panel/interact",
            new
            {
                extensionId,
                panelId,
                interactionType,
                payload,
                targetId,
                selectedCellId = selectedCellId?.ToString()
            });

    public CellVisibilityState ResolveCellVisibility(Guid cellId)
    {
        // Partial implementation: reads user overrides from metadata only (Layer 1).
        // Renderer hint resolution (Layer 2) would require a bridge method.
        var cell = _cells.FirstOrDefault(c => c.Id == cellId);
        var activeLayoutId = _activeLayout?.LayoutId;
        if (cell is null || activeLayoutId is null) return CellVisibilityState.Visible;
        if (!cell.Metadata.TryGetValue(CellLayoutVisibilityMetadata.MetadataKey, out var obj))
            return CellVisibilityState.Visible;

        string? valueStr = null;

        // After JSON round-trip, the value is a JsonElement rather than a typed dictionary
        if (obj is System.Text.Json.JsonElement jsonEl
            && jsonEl.ValueKind == System.Text.Json.JsonValueKind.Object
            && jsonEl.TryGetProperty(activeLayoutId, out var prop)
            && prop.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            valueStr = prop.GetString();
        }
        else if (obj is Dictionary<string, object> dict
            && dict.TryGetValue(activeLayoutId, out var val))
        {
            valueStr = val?.ToString();
        }
        else if (obj is Dictionary<string, string> dictStr)
        {
            dictStr.TryGetValue(activeLayoutId, out valueStr);
        }

        if (valueStr is not null && Enum.TryParse<CellVisibilityState>(valueStr, true, out var state))
            return state;

        return CellVisibilityState.Visible;
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
                HandleCellExecutionState(paramsJson);
                break;
            case "settings/changed":
                OnSettingsChanged?.Invoke();
                break;
            case "variable/changed":
                _ = RefreshVariablesSafeAsync();
                break;
            case "extension/consentRequest":
                HandleConsentRequest(paramsJson);
                break;
            case "extension/changed":
                _ = HandleExtensionChangedAsync();
                break;
            case "output/update":
                HandleOutputUpdate(paramsJson);
                break;
            case "kernel/restarting":
                HandleKernelRestarting(paramsJson);
                break;
            case "kernel/restarted":
                _ = HandleKernelRestartedAsync(paramsJson);
                break;
            case "layout/missing":
                HandleLayoutMissing(paramsJson);
                break;
            case "extension/unavailable":
                HandleExtensionUnavailable(paramsJson);
                break;
            case "layout/updated":
                HandleLayoutUpdated(paramsJson);
                break;
            case "panel/updated":
                HandlePanelUpdated(paramsJson);
                break;
            case "layout/activeChanged":
                HandleActiveLayoutChanged(paramsJson);
                break;
            case "notebook/cellsChanged":
                _ = HandleCellsChangedAsync();
                break;
            case "layout/frameMessage":
                HandleLayoutFrameMessage(paramsJson);
                break;
            case "channel/post":
                HandleOutputChannelPost(paramsJson);
                break;
            case "channel/closed":
                HandleOutputChannelClosed(paramsJson);
                break;
            case "theme/vscodeKindChanged":
                HandleVsCodeThemeKindChanged(paramsJson);
                break;
            case "document/saved":
                // The extension saved the document (webview Save button or Cmd/Ctrl+S in
                // VS Code), so the local unsaved-changes flag is stale either way.
                SetDirty(false);
                break;
            case "host/exited":
                HandleHostExited(paramsJson);
                break;
            case "kernel/faulted":
                HandleKernelFaulted(paramsJson);
                break;
            case "diff/requested":
                // Extension-originated (command palette / editor-title). The extension picks
                // the source natively and passes its id; null falls back to the UI's picker.
                OnDiffRequested?.Invoke(TryReadStringProperty(paramsJson, "sourceId"));
                break;
        }
    }

    private void HandleHostExited(string? paramsJson)
    {
        var detail = TryReadStringProperty(paramsJson, "detail");
        OnKernelHealthChanged?.Invoke(new KernelHealthChangedEventArgs(
            KernelHealth.Disconnected,
            detail ?? UI.Kernel_HostExited));
    }

    private void HandleKernelFaulted(string? paramsJson)
    {
        var message = TryReadStringProperty(paramsJson, "message");
        OnKernelHealthChanged?.Invoke(new KernelHealthChangedEventArgs(
            KernelHealth.Faulted, message));
    }

    /// <summary>
    /// The VS Code color theme changed. The actual colors live in the webview page's
    /// --vscode-* variables (which the isolated-frame interop reads when posting), so
    /// here we just track the kind and raise <see cref="OnThemeChanged"/> so the active
    /// isolated layout frame is re-sent the current theme.
    /// </summary>
    private void HandleVsCodeThemeKindChanged(string? paramsJson)
    {
        var kind = TryReadStringProperty(paramsJson, "kind");
        if (string.Equals(kind, "dark", StringComparison.OrdinalIgnoreCase))
            _activeThemeKind = ThemeKind.Dark;
        else if (string.Equals(kind, "light", StringComparison.OrdinalIgnoreCase))
            _activeThemeKind = ThemeKind.Light;

        OnThemeChanged?.Invoke();
    }

    private void HandleLayoutFrameMessage(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return;

        try
        {
            using var doc = JsonDocument.Parse(paramsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("frameInstanceId", out var idEl)
                || idEl.ValueKind != JsonValueKind.String)
                return;
            if (!root.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String)
                return;

            JsonElement? payload = root.TryGetProperty("payload", out var p)
                ? p.Clone()
                : null;

            OnLayoutFrameMessage?.Invoke(new LayoutFrameMessageEventArgs(
                idEl.GetString() ?? string.Empty,
                typeEl.GetString() ?? string.Empty,
                payload));
        }
        catch (JsonException) { }
    }

    /// <summary>
    /// A message from a channel's owner on its way to the view drawing it. Handed straight to the
    /// interop, which is the only part that knows which frame is currently drawing a given channel,
    /// so nothing on the page has to be involved and no component has to be alive to receive one.
    /// </summary>
    private void HandleOutputChannelPost(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return;

        try
        {
            using var doc = JsonDocument.Parse(paramsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("channelId", out var idEl)
                || idEl.ValueKind != JsonValueKind.String)
                return;
            if (!root.TryGetProperty("messageType", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String)
                return;

            // Cloned because the document is disposed when this method returns and the call into
            // JS outlives it.
            JsonElement? payload = root.TryGetProperty("payload", out var p)
                ? p.Clone()
                : null;

            _ = PostToOutputChannelAsync(
                idEl.GetString() ?? string.Empty,
                typeEl.GetString() ?? string.Empty,
                payload);
        }
        catch (JsonException) { }
    }

    /// <summary>
    /// The channel behind a view is gone. The view is told so it can stop expecting answers and
    /// show itself as no longer live.
    /// </summary>
    private void HandleOutputChannelClosed(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return;

        var channelId = TryReadStringProperty(paramsJson, "channelId");
        if (string.IsNullOrEmpty(channelId)) return;

        _ = CloseOutputChannelAsync(
            channelId, TryReadStringProperty(paramsJson, "reason") ?? string.Empty);
    }

    private async Task PostToOutputChannelAsync(string channelId, string messageType, JsonElement? payload)
    {
        try
        {
            await _js.InvokeVoidAsync(
                "versoOutputChannel.post", channelId, messageType, payload);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }

    private async Task CloseOutputChannelAsync(string channelId, string reason)
    {
        try
        {
            await _js.InvokeVoidAsync("versoOutputChannel.closed", channelId, reason);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }

    private void HandleKernelRestarting(string? paramsJson)
    {
        var kernelId = TryReadStringProperty(paramsJson, "kernelId");
        OnKernelRestarting?.Invoke(kernelId);
    }

    private async Task HandleKernelRestartedAsync(string? paramsJson)
    {
        var kernelId = TryReadStringProperty(paramsJson, "kernelId");

        // The fresh host has reopened the notebook from the snapshot, so re-pull
        // everything that depends on host state. Variables are guaranteed empty;
        // extensions loaded via #!nuget or #!extension are gone until those cells
        // re-execute; layouts and themes default back to the built-in set.
        try { await RefreshCellListAsync(); } catch { /* swallow — UI stays usable */ }
        try { await RefreshExtensionDataAsync(); } catch { /* same */ }
        try { await RefreshThemeDataAsync(); } catch { /* same */ }
        try { await RefreshVariablesAsync(); } catch { /* same */ }

        OnNotebookChanged?.Invoke();
        OnVariablesChanged?.Invoke();
        OnExtensionStatusChanged?.Invoke();
        OnLayoutChanged?.Invoke();
        OnKernelRestarted?.Invoke(kernelId);
    }

    private void HandleLayoutMissing(string? paramsJson)
    {
        var layoutId = TryReadStringProperty(paramsJson, "layoutId");
        if (layoutId is not null)
            OnLayoutMissing?.Invoke(layoutId);
    }

    private void HandleExtensionUnavailable(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson))
            return;

        try
        {
            using var doc = JsonDocument.Parse(paramsJson);
            if (!doc.RootElement.TryGetProperty("extensions", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return;

            var list = new List<UnavailableExtensionInfo>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var packageId = item.TryGetProperty("packageId", out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(packageId))
                    continue;
                var version = item.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;
                var reason = item.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString() ?? string.Empty
                    : string.Empty;
                list.Add(new UnavailableExtensionInfo(packageId!, version, reason));
            }

            if (list.Count > 0)
            {
                foreach (var u in list)
                    _unavailableExtensionReasons[u.PackageId] = u.Reason;

                // The installed list may already be cached from extension/list; stamp the reason onto
                // matching rows so the panel flags them without waiting for a refresh.
                for (var i = 0; i < _installedExtensions.Count; i++)
                {
                    var ext = _installedExtensions[i];
                    if (_unavailableExtensionReasons.TryGetValue(ext.Id, out var reason)
                        && ext.UnavailableReason != reason)
                        _installedExtensions[i] = ext with { UnavailableReason = reason };
                }

                OnRequiredExtensionsUnavailable?.Invoke(list);
                OnExtensionStatusChanged?.Invoke();
            }
        }
        catch (JsonException) { }
    }

    private void HandlePanelUpdated(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return;

        PanelUpdatedNotification? notif;
        try
        {
            notif = JsonSerializer.Deserialize<PanelUpdatedNotification>(
                paramsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return;
        }

        if (notif is null || string.IsNullOrEmpty(notif.ExtensionId) || string.IsNullOrEmpty(notif.PanelId))
            return;

        OnPanelUpdated?.Invoke(new PanelUpdatedEventArgs(notif.ExtensionId, notif.PanelId));
    }

    private void HandleLayoutUpdated(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return;

        LayoutUpdatedNotification? notif;
        try
        {
            notif = JsonSerializer.Deserialize<LayoutUpdatedNotification>(
                paramsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return;
        }

        if (notif is null || string.IsNullOrEmpty(notif.ExtensionId) || string.IsNullOrEmpty(notif.LayoutId))
            return;

        Guid? cellId = null;
        if (!string.IsNullOrEmpty(notif.CellId) && Guid.TryParse(notif.CellId, out var parsed))
            cellId = parsed;

        OnLayoutUpdated?.Invoke(new LayoutUpdatedEventArgs(
            notif.ExtensionId,
            notif.LayoutId,
            notif.FrameInstanceId ?? string.Empty,
            notif.Scope ?? "full",
            cellId));
    }

    /// <summary>
    /// The host's active layout changed outside this client's own
    /// <see cref="SwitchLayoutAsync(LayoutReference)"/> call (for example the VS Code
    /// chat participant invoked <c>layout/switch</c> directly). Re-sync the cached
    /// active layout and swap the rendered view. Unlike <c>layout/updated</c>, which
    /// re-renders the already-active layout, this changes which layout is active.
    /// </summary>
    private void HandleActiveLayoutChanged(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return;

        LayoutActiveChangedNotification? notif;
        try
        {
            notif = JsonSerializer.Deserialize<LayoutActiveChangedNotification>(
                paramsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return;
        }

        if (notif is null || string.IsNullOrEmpty(notif.LayoutId)) return;

        var reference = new LayoutReference(notif.ExtensionId ?? string.Empty, notif.LayoutId);

        // Idempotent: a switch this client initiated already updated local state, so an
        // echo for the layout that is already active is a no-op (avoids a redundant
        // re-render).
        if (_activeLayout is { } current &&
            string.Equals(current.LayoutId, reference.LayoutId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.ExtensionId, reference.ExtensionId, StringComparison.OrdinalIgnoreCase))
            return;

        ApplyActiveLayout(reference);
    }

    private static string? TryReadStringProperty(string? json, string property)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private async Task HandleExtensionChangedAsync()
    {
        // Drop cached blob URLs so a reloaded extension re-fetches its assets at the
        // next layout mount. The JS-side blob map is cleared as a side effect of the
        // host component's release-on-switch hook firing during the resulting layout
        // refresh; we drop our key map here so cache hits are not served from stale
        // entries the JS side already revoked.
        _layoutAssetUrlCache.Clear();

        await RefreshExtensionDataAsync();
        OnExtensionStatusChanged?.Invoke();
        OnLayoutChanged?.Invoke();
        OnNotebookChanged?.Invoke();
    }

    private void HandleOutputUpdate(string? paramsJson)
    {
        if (!string.IsNullOrWhiteSpace(paramsJson))
        {
            try
            {
                var notif = JsonSerializer.Deserialize<OutputUpdateNotification>(
                    paramsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (notif is not null && Guid.TryParse(notif.CellId, out var cellId))
                {
                    var cell = _cells.FirstOrDefault(c => c.Id == cellId);
                    if (cell is not null)
                    {
                        cell.Outputs.Clear();
                        foreach (var output in notif.Outputs ?? Enumerable.Empty<CellOutputDto>())
                            cell.Outputs.Add(MapOutputFromDto(output));

                        NotifyOutputUpdatedCoalesced();
                        RaiseCellOutputUpdated(paramsJson!, cellId);
                        return;
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to a full refresh below.
            }
        }

        _ = RefreshCellListAsync().ContinueWith(_ => OnOutputUpdated?.Invoke());
    }

    private void RaiseCellOutputUpdated(string paramsJson, Guid cellId)
    {
        if (OnCellOutputUpdated is null) return;
        try
        {
            using var doc = JsonDocument.Parse(paramsJson);
            if (doc.RootElement.TryGetProperty("outputs", out var outputsEl))
            {
                OnCellOutputUpdated.Invoke(new CellOutputUpdatedEventArgs(cellId, outputsEl.Clone()));
            }
        }
        catch (JsonException) { }
    }

    // Throttle the whole-page output-update notification: schedule at most one raise per coalescing
    // window during a burst. The trailing raise ensures the final output state is rendered.
    private void NotifyOutputUpdatedCoalesced()
    {
        if (_outputNotifyScheduled) return;
        _outputNotifyScheduled = true;
        _ = FlushOutputNotifyAsync();
    }

    private async Task FlushOutputNotifyAsync()
    {
        await Task.Delay(OutputNotifyCoalesceMs);
        _outputNotifyScheduled = false;
        OnOutputUpdated?.Invoke();
    }

    private void HandleCellExecutionState(string? paramsJson)
    {
        if (paramsJson is null) return;

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var notif = JsonSerializer.Deserialize<ExecutionStateNotification>(paramsJson, opts);
        if (notif is null || !Guid.TryParse(notif.CellId, out var id)) return;

        if (string.Equals(notif.State, "running", StringComparison.OrdinalIgnoreCase))
        {
            OnCellExecuting?.Invoke(id);
        }
        else
        {
            // Only the per-cell completion event fires here. The parameterless OnCellExecuted is
            // reserved for non-execution mutations (clear-outputs, toolbar actions); firing both on
            // completion made every subscriber render twice per finished cell, which during Run All
            // multiplied into a whole-page render storm.
            OnCellExecutionCompleted?.Invoke(id);
        }
    }

    private void HandleConsentRequest(string? paramsJson)
    {
        if (paramsJson is null) return;

        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var request = JsonSerializer.Deserialize<ConsentRequestNotification>(paramsJson, opts);
        if (request?.RequestId is null) return;

        _pendingConsentRequestId = request.RequestId;
        _pendingConsentExtensions = request.Extensions?
            .Select(e => new ExtensionConsentInfo(
                e.PackageId ?? "",
                e.Version,
                e.Source ?? "cell")
            {
                Kind = string.Equals(e.Kind, "package", StringComparison.OrdinalIgnoreCase)
                    ? ConsentKind.Package
                    : ConsentKind.Extension,
                Target = e.Target,
            })
            .ToList()
            ?? new List<ExtensionConsentInfo>();

        OnExtensionConsentRequested?.Invoke();
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

        // Drop any prior notebook's load failures so they don't flag rows in this one; the new
        // notebook's required-extension load reports its own via extension/unavailable.
        _unavailableExtensionReasons.Clear();

        // Seed the active layout from the open payload (when the host provides it) so the
        // first render already selects the correct layout renderer. Without this the layout
        // arrives with the deferred extension-data refresh below, changing ActiveLayoutKey
        // mid-load and re-keying the whole layout subtree, which shows as a visible flash
        // while every cell re-mounts. Older hosts omit the field; they keep the old
        // behavior of resolving the layout during the refresh.
        if (result.ActiveLayout is { } activeLayout)
            _layouts = new List<LayoutInfo> { MapLayoutFromDto(activeLayout) };

        // Notify immediately so the UI can render cells while supplementary data loads
        OnNotebookChanged?.Invoke();

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

    // The host signalled that its cell collection changed from an operation we did not initiate
    // directly (e.g. a custom layout's own insert/move/delete affordance). Re-pull the cell list
    // and announce it so the cell pool re-renders and the layout re-portals the affected cells.
    private async Task HandleCellsChangedAsync()
    {
        try { await RefreshCellListAsync(); }
        catch { return; /* leave the cache as-is; the UI stays usable */ }
        OnNotebookChanged?.Invoke();
    }

    private async Task RefreshExtensionDataAsync()
    {
        // Toolbar actions
        var actionsResult = await _bridge.RequestAsync<ToolbarActionsResponse>("notebook/getToolbarActions", null);
        _toolbarActions = actionsResult.Actions?.Select(a => new ToolbarActionInfo(
            a.ActionId, a.DisplayName, a.Icon,
            Enum.TryParse<ToolbarPlacement>(a.Placement, true, out var p) ? p : ToolbarPlacement.MainToolbar,
            a.Order, a.IconOnly, a.IsPrimary, a.ConfirmationPrompt, a.Description)).ToList() ?? new();

        // Cell types
        var cellTypesResult = await _bridge.RequestAsync<CellTypesResponse>("notebook/getCellTypes", null);
        _cellTypes = cellTypesResult.CellTypes?.Select(ct => new CellTypeInfo(ct.Id, ct.DisplayName, ct.IsEditable)).ToList() ?? new();

        // Languages
        var langsResult = await _bridge.RequestAsync<LanguagesResponse>("notebook/getLanguages", null);
        _registeredLanguages = langsResult.Languages?.Select(l => new KernelLanguageInfo(l.Id, l.DisplayName, l.SupportsCancellation)).ToList() ?? new();

        // Layouts
        var layoutsResult = await _bridge.RequestAsync<LayoutsResponse>("layout/getLayouts", null);
        _layouts = layoutsResult.Layouts?.Select(MapLayoutFromDto).ToList() ?? new();

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
        _installedExtensions = extsResult.Installed?
            .Select(i => new InstalledExtensionDto(
                i.Id ?? "", i.Version, i.IsLocal,
                _unavailableExtensionReasons.GetValueOrDefault(i.Id ?? ""),
                i.Capabilities, i.IconDataUri))
            .Where(i => !string.IsNullOrEmpty(i.Id))
            .ToList() ?? new();
        _marketplaceSources = extsResult.Sources ?? new();

        // Settings
        await RefreshSettingsAsync();

        // Variables
        await RefreshVariablesSafeAsync();
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
                    MapSpacingFromDto(theme.Spacing),
                    MapElevationFromDto(theme.Elevation));
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

    /// <summary>
    /// Public refresh — sends variable/list to the host and updates cache.
    /// Throws on failure so callers (e.g. UI Refresh button) can surface the error.
    /// </summary>
    public async Task RefreshVariablesAsync()
    {
        var result = await _bridge.RequestAsync<VariableListResponse>("variable/list", null);
        _variablesCache = result.Variables?.Select(v =>
            new VariableEntryDto(v.Name, v.TypeName, v.ValuePreview, v.IsExpandable)).ToList() ?? new();
        OnVariablesChanged?.Invoke();
    }

    /// <summary>
    /// Internal refresh — catches and logs errors so background/notification callers don't crash.
    /// </summary>
    private async Task RefreshVariablesSafeAsync()
    {
        try
        {
            await RefreshVariablesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RemoteNotebookService] RefreshVariablesAsync failed: {ex.Message}");
        }
    }

    // ── DTO mapping ─────────────────────────────────────────────────────

    /// <summary>
    /// Maps a layout wire item to <see cref="LayoutInfo"/>. When the item is flagged
    /// active, also records it as the current layout so <see cref="ActiveLayoutKey"/>
    /// and the renderer-selection properties (capabilities, isolation, properties
    /// panel) reflect it.
    /// </summary>
    private LayoutInfo MapLayoutFromDto(LayoutItem l)
    {
        var isolation = string.IsNullOrEmpty(l.RendererIsolation) ? "inline" : l.RendererIsolation;
        if (l.IsActive)
        {
            _activeLayout = string.IsNullOrEmpty(l.ExtensionId)
                ? new LayoutReference(string.Empty, l.Id)
                : new LayoutReference(l.ExtensionId, l.Id);
            _isDashboardLayout = l.RequiresCustomRenderer;
            _layoutCapabilities = (LayoutCapabilities)l.Capabilities;
            _activeLayoutSupportsPropertiesPanel = l.SupportsPropertiesPanel;
            _activeLayoutRendererIsolation = isolation;
        }
        return new LayoutInfo(l.Id, l.DisplayName, l.RequiresCustomRenderer,
            (LayoutCapabilities)l.Capabilities, l.SupportsPropertiesPanel, l.ExtensionId ?? string.Empty,
            isolation);
    }

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
        if (dto.Metadata is not null)
        {
            foreach (var kv in dto.Metadata)
                cell.Metadata[kv.Key] = kv.Value;
        }
        return cell;
    }

    private static CellOutput MapOutputFromDto(CellOutputDto dto)
    {
        return new CellOutput(dto.MimeType, dto.Content, dto.IsError, dto.ErrorName, dto.ErrorStackTrace)
        {
            Channel = dto.Channel?.ToLowerInvariant() switch
            {
                "stdout" => OutputChannel.Stdout,
                "stderr" => OutputChannel.Stderr,
                _ => null
            },
            LiveChannelId = dto.LiveChannelId
        };
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
            AccentForeground = G(nameof(d.AccentForeground), d.AccentForeground),
            BgDefault = G(nameof(d.BgDefault), d.BgDefault),
            BgElevated = G(nameof(d.BgElevated), d.BgElevated),
            BgSunken = G(nameof(d.BgSunken), d.BgSunken),
            FgDefault = G(nameof(d.FgDefault), d.FgDefault),
            FgMuted = G(nameof(d.FgMuted), d.FgMuted),
            FgSubtle = G(nameof(d.FgSubtle), d.FgSubtle),
            // Accent derives from AccentPrimary (read-only alias), so it is not mapped here.
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
        var fallback = new ThemeSpacing();
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
            ScrollbarWidth = dto.ScrollbarWidth,
            // A host that predates the shape scale sends nothing rather than zero, so
            // fall back to the model defaults instead of squaring every corner.
            ShapeSmall = dto.ShapeSmall ?? fallback.ShapeSmall,
            ShapeMedium = dto.ShapeMedium ?? fallback.ShapeMedium,
            ShapeLarge = dto.ShapeLarge ?? fallback.ShapeLarge,
            ShapeFull = dto.ShapeFull ?? fallback.ShapeFull
        };
    }

    private static ThemeElevation MapElevationFromDto(ThemeElevationResponse? dto)
    {
        if (dto is null) return new ThemeElevation();
        var fallback = new ThemeElevation();
        return new ThemeElevation
        {
            Level0 = dto.Level0 ?? fallback.Level0,
            Level1 = dto.Level1 ?? fallback.Level1,
            Level2 = dto.Level2 ?? fallback.Level2,
            Level3 = dto.Level3 ?? fallback.Level3
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
        // Resolved active layout forwarded from the host's notebook/open response.
        // Null when the host predates the field or no layout is registered.
        public LayoutItem? ActiveLayout { get; set; }
    }

    private sealed class ExecutionStateNotification
    {
        public string CellId { get; set; } = "";
        public string State { get; set; } = "";
    }

    private sealed class OutputUpdateNotification
    {
        public string CellId { get; set; } = "";
        public List<CellOutputDto>? Outputs { get; set; }
    }

    private sealed class LayoutUpdatedNotification
    {
        public string ExtensionId { get; set; } = "";
        public string LayoutId { get; set; } = "";
        public string? FrameInstanceId { get; set; }
        public string? Scope { get; set; }
        public string? CellId { get; set; }
    }

    private sealed class PanelUpdatedNotification
    {
        public string ExtensionId { get; set; } = "";
        public string PanelId { get; set; } = "";
    }

    private sealed class PanelListResponse
    {
        public List<PanelInfoResponse>? Panels { get; set; }
    }

    private sealed class PanelInfoResponse
    {
        public string PanelId { get; set; } = "";
        public string ExtensionId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? IconName { get; set; }
        public string? IconMarkup { get; set; }
        public int Order { get; set; }
        public string? Description { get; set; }
    }

    private sealed class PanelRenderResponse
    {
        public List<PanelRepresentationResponse>? Representations { get; set; }
    }

    private sealed class PanelRepresentationResponse
    {
        public string MimeType { get; set; } = "";
        public string Content { get; set; } = "";
    }

    private sealed class LayoutActiveChangedNotification
    {
        public string? ExtensionId { get; set; }
        public string LayoutId { get; set; } = "";
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
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// Mirrors <c>Verso.Host</c>'s CellOutputDto. The two are separate declarations with no shared
    /// type, so a field added to one has to be added to the other or it silently fails to arrive.
    /// </summary>
    private sealed class CellOutputDto
    {
        public string MimeType { get; set; } = "";
        public string Content { get; set; } = "";
        public bool IsError { get; set; }
        public string? ErrorName { get; set; }
        public string? ErrorStackTrace { get; set; }
        public string? Channel { get; set; }
        public string? LiveChannelId { get; set; }
    }

    private sealed class CellListResponse
    {
        public List<CellDto>? Cells { get; set; }
    }

    private sealed class ExecutionResponse
    {
        public string? Status { get; set; }
        public int ExecutionCount { get; set; }
        public double ElapsedMs { get; set; }
        public List<CellOutputDto>? Outputs { get; set; }
        public bool Dirty { get; set; } = true;
    }

    private sealed class ExecutionRunAllResponse
    {
        public List<ExecutionRunAllResultDto>? Results { get; set; }
    }

    private sealed class ExecutionRunAllResultDto
    {
        public string CellId { get; set; } = "";
        public string? Status { get; set; }
        public int ExecutionCount { get; set; }
        public double ElapsedMs { get; set; }
        public List<CellOutputDto>? Outputs { get; set; }
        public bool Dirty { get; set; } = true;
    }

    private sealed class CellTypesResponse
    {
        public List<CellTypeResponseDto>? CellTypes { get; set; }
    }

    private sealed class CellTypeResponseDto
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsEditable { get; set; } = true;
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
        public bool IconOnly { get; set; }
        public bool IsPrimary { get; set; }
        public string? ConfirmationPrompt { get; set; }
        public string? Description { get; set; }
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
        public bool SupportsCancellation { get; set; } = true;
    }

    private sealed class LayoutsResponse
    {
        public List<LayoutItem>? Layouts { get; set; }
    }

    private sealed class LayoutRenderResponse
    {
        public string Html { get; set; } = "";
    }

    private sealed class LayoutRendererPackageResponse
    {
        public string EntryPoint { get; set; } = "";
        public Dictionary<string, string>? Files { get; set; }
        public string? ContentSecurityPolicy { get; set; }
        public string? RendererProtocolVersion { get; set; }
    }

    private sealed class LayoutStaticAssetsResponse
    {
        public List<LayoutStaticAssetItem>? Assets { get; set; }
    }

    private sealed class LayoutStaticAssetItem
    {
        public string AssetId { get; set; } = "";
        public string ContentType { get; set; } = "";
        // Base64-encoded asset bytes.
        public string Content { get; set; } = "";
        public LayoutStaticAssetLoadHintsItem? LoadHints { get; set; }
        public string? ContentSecurityPolicy { get; set; }
    }

    private sealed class LayoutStaticAssetLoadHintsItem
    {
        public string? ModuleKind { get; set; }
        public string? LoadMode { get; set; }
        public string? Placement { get; set; }
    }

    private sealed class AllocateFrameInstanceResponse
    {
        public string FrameInstanceId { get; set; } = "";
    }

    private sealed class LayoutRendererMountedResponse
    {
        public Dictionary<string, JsonElement>? Extension { get; set; }
    }

    private sealed class LayoutItem
    {
        public string Id { get; set; } = "";
        public string? ExtensionId { get; set; }
        public string DisplayName { get; set; } = "";
        public bool RequiresCustomRenderer { get; set; }
        public string? RendererIsolation { get; set; }
        public bool IsActive { get; set; }
        public int Capabilities { get; set; }
        public bool SupportsPropertiesPanel { get; set; }
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
        public List<InstalledExtensionItemResponse>? Installed { get; set; }
        public List<string>? Sources { get; set; }
    }

    private sealed class InstalledExtensionItemResponse
    {
        public string? Id { get; set; }
        public string? Version { get; set; }
        public bool IsLocal { get; set; }

        // Null and empty mean different things here: not loaded yet, versus loaded and
        // contributed no extension point. Keep the null rather than defaulting to a list.
        public List<string>? Capabilities { get; set; }

        // The package's own icon, already inlined as a data URI by the host so the client
        // never reaches the network for it.
        public string? IconDataUri { get; set; }
    }

    private sealed class ThemeDataResponse
    {
        public Dictionary<string, string>? Colors { get; set; }
        public Dictionary<string, string>? SyntaxColors { get; set; }
        public ThemeTypographyResponse? Typography { get; set; }
        public ThemeSpacingResponse? Spacing { get; set; }
        public ThemeElevationResponse? Elevation { get; set; }
    }

    private sealed class ThemeElevationResponse
    {
        public string? Level0 { get; set; }
        public string? Level1 { get; set; }
        public string? Level2 { get; set; }
        public string? Level3 { get; set; }
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
        public double? ShapeSmall { get; set; }
        public double? ShapeMedium { get; set; }
        public double? ShapeLarge { get; set; }
        public double? ShapeFull { get; set; }
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

    private sealed class ExtensionSearchResponse
    {
        public List<PackageSearchItemResponse>? Packages { get; set; }
    }

    private sealed class PackageSearchItemResponse
    {
        public string? Id { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string? Authors { get; set; }
        public long? DownloadCount { get; set; }
        public string? IconUrl { get; set; }
        public string? ProjectUrl { get; set; }
        public bool IsInstalled { get; set; }
    }

    private sealed class ExtensionVersionsResponse
    {
        public List<string>? Versions { get; set; }
    }

    private sealed class ExtensionInstallResponse
    {
        public bool Success { get; set; }
        public string? PackageId { get; set; }
        public string? ResolvedVersion { get; set; }
        public string? ErrorMessage { get; set; }
        public int ExtensionsRegistered { get; set; }
    }

    private sealed class BrowseFileResponse
    {
        public string? Path { get; set; }
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

    private sealed class ConsentRequestNotification
    {
        public string? RequestId { get; set; }
        public List<ConsentExtensionItem>? Extensions { get; set; }
    }

    private sealed class ConsentExtensionItem
    {
        public string? PackageId { get; set; }
        public string? Version { get; set; }
        public string? Source { get; set; }
        public string? Kind { get; set; }
        public string? Target { get; set; }
    }

    private sealed class CellInteractResponse
    {
        public string? Response { get; set; }
        public bool Dirty { get; set; }
    }

    private sealed class PropertiesGetSectionsResponse
    {
        public List<PropertySectionResultResponse>? Sections { get; set; }
    }

    private sealed class PropertySectionResultResponse
    {
        public string ProviderExtensionId { get; set; } = "";
        public PropertySectionResponse? Section { get; set; }
    }

    private sealed class PropertySectionResponse
    {
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public List<PropertyFieldResponse>? Fields { get; set; }
    }

    private sealed class PropertyFieldResponse
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FieldType { get; set; } = "";
        public object? CurrentValue { get; set; }
        public string? Description { get; set; }
        public bool IsReadOnly { get; set; }
        public List<PropertyFieldOptionResponse>? Options { get; set; }
    }

    private sealed class PropertyFieldOptionResponse
    {
        public string Value { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    private sealed class DiffSourcesResponse
    {
        public List<DiffSourceItem>? Sources { get; set; }
    }

    private sealed class DiffSourceItem
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string? Kind { get; set; }
        public bool Available { get; set; }
        public string? Description { get; set; }
    }

    private sealed class DiffBaselineResponse
    {
        public bool? Cancelled { get; set; }
        public string? Content { get; set; }
        public string? FilePath { get; set; }

        /// <summary>Which of the baselines this is, so it can be named here.</summary>
        public string? LabelKind { get; set; }

        /// <summary>
        /// The part of the name that is not a word: a git ref, or a file name.
        /// </summary>
        public string? LabelArg { get; set; }
    }
}
