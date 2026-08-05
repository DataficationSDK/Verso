using System.Collections.Concurrent;
using Verso.Abstractions;
using Verso.Contexts;
using Verso.Execution;
using Verso.Extensions;
using Verso.Stubs;
using Verso.Resources;

namespace Verso;

/// <summary>
/// Core orchestrator: cell CRUD, kernel registry, execution dispatch, shared state, and subsystem hooks.
/// </summary>
public sealed class Scaffold : IAsyncDisposable
{
    private readonly NotebookModel _notebook;
    private readonly object _cellLock = new();
    private readonly Dictionary<string, ILanguageKernel> _kernels = new(StringComparer.OrdinalIgnoreCase);
    // Lazy<Task> with ExecutionAndPublication ensures the init factory runs exactly
    // once per kernel even if WarmUpKernelAsync and the execution pipeline's
    // EnsureInitialized race on a fresh notebook open. ConcurrentDictionary.GetOrAdd
    // alone does not provide this guarantee.
    private readonly ConcurrentDictionary<string, Lazy<Task>> _initializationTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, int> _executionCounts = new();
    private readonly VariableStore _variables = new();
    private readonly NotebookMetadataContext _metadata;
    private readonly StubExtensionHostContext _stubExtensionHost;
    private readonly ExtensionHost? _extensionHost;
    private ThemeEngine? _themeEngine;
    private LayoutManager? _layoutManager;
    private SettingsManager? _settingsManager;
    private readonly NotebookOperations _notebookOps;

    /// <summary>
    /// This notebook's share of the failures raised by work its cells started and did not wait for.
    /// Held per notebook rather than per process so a fault cannot be reported against a notebook
    /// running alongside this one in the same host.
    /// </summary>
    private readonly BackgroundFaultSink _backgroundFaults = new();
    private readonly IDisposable _backgroundFaultRegistration;

    /// <summary>
    /// Channels between this notebook's outputs and whatever is drawing them. Held here rather
    /// than by an execution because that is the only lifetime long enough to be useful: a channel
    /// matters most when no cell is running and someone is interacting with what the last one drew.
    /// </summary>
    private readonly OutputChannelHost _outputChannels = new();

    private bool _disposed;

    public Scaffold() : this(new NotebookModel()) { }

    public Scaffold(NotebookModel notebook) : this(notebook, extensionHost: null) { }

    public Scaffold(NotebookModel notebook, ExtensionHost? extensionHost, string? filePath = null)
    {
        // Every host builds a Scaffold, so this is where watching for failures on work a cell
        // started and did not wait for gets turned on. Installing is idempotent.
        BackgroundFaultMonitor.Install();
        _backgroundFaultRegistration = BackgroundFaultMonitor.Register(_backgroundFaults);

        _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
        _metadata = new NotebookMetadataContext(_notebook, filePath);
        _extensionHost = extensionHost;
        _stubExtensionHost = new StubExtensionHostContext(() => _kernels.Values.ToList());
        _notebookOps = new NotebookOperations(this);
    }

    // --- Properties ---

    public IReadOnlyList<CellModel> Cells
    {
        get { lock (_cellLock) { return _notebook.Cells.ToList(); } }
    }

    public IVariableStore Variables => _variables;
    public NotebookModel Notebook => _notebook;
    public string? Title { get => _notebook.Title; set => _notebook.Title = value; }
    public string? DefaultKernelId { get => _notebook.DefaultKernelId; set => _notebook.DefaultKernelId = value; }

    /// <summary>
    /// Gets the active theme context. Delegates to the <see cref="ThemeEngine"/> if initialized,
    /// otherwise falls back to <see cref="StubThemeContext"/>.
    /// </summary>
    public IThemeContext ThemeContext => _themeEngine as IThemeContext ?? new StubThemeContext();

    /// <summary>
    /// Gets the layout capabilities from the active layout, or all capabilities if no LayoutManager is active.
    /// </summary>
    public LayoutCapabilities LayoutCapabilities =>
        _layoutManager?.Capabilities ?? (LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete |
                             LayoutCapabilities.CellReorder | LayoutCapabilities.CellEdit |
                             LayoutCapabilities.CellResize | LayoutCapabilities.CellExecute |
                             LayoutCapabilities.MultiSelect);

    public IExtensionHostContext ExtensionHostContext =>
        _extensionHost as IExtensionHostContext ?? _stubExtensionHost;

    /// <summary>
    /// Gets the <see cref="ThemeEngine"/> subsystem, or <c>null</c> if not initialized.
    /// </summary>
    public ThemeEngine? ThemeEngine => _themeEngine;

    /// <summary>
    /// Gets the <see cref="LayoutManager"/> subsystem, or <c>null</c> if not initialized.
    /// </summary>
    public LayoutManager? LayoutManager => _layoutManager;

    /// <summary>
    /// Gets the <see cref="SettingsManager"/> subsystem, or <c>null</c> if not initialized.
    /// </summary>
    public SettingsManager? SettingsManager => _settingsManager;

    /// <summary>
    /// Gets the <see cref="INotebookOperations"/> implementation for this scaffold.
    /// </summary>
    public INotebookOperations NotebookOps => _notebookOps;

    /// <summary>
    /// Gets this notebook's output channels. A host that can route messages to a rendered output
    /// attaches an <see cref="IOutputChannelTransport"/> here; one that cannot leaves it alone, and
    /// the contexts handed to kernels report no channels at all so they write static output.
    /// </summary>
    public OutputChannelHost OutputChannels => _outputChannels;

    /// <summary>
    /// Optional external handler invoked instead of the in-process kernel restart.
    /// Set by host supervisors (e.g. the VS Code extension) that need to kill and
    /// respawn the entire process to release native resources like file handles
    /// pinned by the default AssemblyLoadContext.
    ///
    /// When set, <see cref="RestartKernelAsync"/> calls this handler and skips
    /// the in-process dispose/reinit cycle. When null, the in-process restart
    /// runs (used by CLI, REPL, and server hosts).
    /// </summary>
    public Func<string?, Task>? HostRestartHandler { get; set; }

    /// <summary>
    /// Updates the notebook file path used for resolving relative paths (e.g. in <c>#!import</c>).
    /// This is called after construction when the file path is not available at open time.
    /// </summary>
    public void SetFilePath(string? filePath)
    {
        _metadata.FilePath = filePath;
    }

    /// <summary>
    /// The notebook's current file path, or <c>null</c> when the notebook has no backing file.
    /// </summary>
    public string? FilePath => _metadata.FilePath;

    /// <summary>
    /// Notebook metadata in the form extensions see it, for hosts building an
    /// <see cref="IVersoContext"/> outside the execution pipeline.
    /// </summary>
    public INotebookMetadata Metadata => _metadata;

    // --- Subsystem initialization ---

    /// <summary>
    /// Initializes the ThemeEngine and LayoutManager from extensions discovered by the ExtensionHost.
    /// Call after <see cref="ExtensionHost.LoadBuiltInExtensionsAsync"/>.
    /// </summary>
    public void InitializeSubsystems()
    {
        if (_extensionHost is null) return;

        var themes = _extensionHost.GetThemes();
        var layouts = _extensionHost.GetLayouts();

        _themeEngine = new ThemeEngine(themes, _notebook.PreferredThemeId);
        _layoutManager = new LayoutManager(layouts, _notebook.ActiveLayout);

        // If the notebook was loaded with an unqualified (legacy) layout reference and the
        // LayoutManager resolved it unambiguously, promote the in-memory reference to the
        // qualified form so the next save writes the qualified shape. When resolution failed
        // (no match or ambiguous), leave the reference unqualified so the bare string is
        // round-tripped to disk for the missing extension to resolve on a later load.
        //
        // This is host-dependent resolution: it needs the loaded extension set to discover the
        // owning extension id, so it lives here at wire-up rather than in the pure-data format
        // migration pipeline that runs inside the serializer.
        if (_notebook.RequiresLegacyLayoutResolution &&
            _notebook.ActiveLayout is { IsUnqualified: true } &&
            _layoutManager.ActiveLayout is { } resolved &&
            resolved is IExtension resolvedExt &&
            string.Equals(resolved.LayoutId, _notebook.ActiveLayout.Value.LayoutId, StringComparison.OrdinalIgnoreCase))
        {
            _notebook.ActiveLayout = new LayoutReference(resolvedExt.ExtensionId, resolved.LayoutId);
            _notebook.RequiresLegacyLayoutResolution = false;
        }

        var settable = _extensionHost.GetSettableExtensions();
        _settingsManager = new SettingsManager(settable);

        // When extensions are loaded dynamically (e.g. via #!extension) or
        // enabled/disabled at runtime, refresh subsystems so the layout manager,
        // theme engine, and settings manager reflect the current enabled set.
        _extensionHost.OnExtensionLoaded += _ => RefreshSubsystems();
        _extensionHost.OnExtensionStatusChanged += (_, _) => RefreshSubsystems();
    }

    /// <summary>
    /// Re-queries the <see cref="ExtensionHost"/> for the latest capabilities and
    /// updates every subsystem.  Preserves the currently active theme and layout.
    /// </summary>
    private void RefreshSubsystems()
    {
        if (_extensionHost is null) return;

        _layoutManager?.Refresh(_extensionHost.GetLayouts());
        _themeEngine?.Refresh(_extensionHost.GetThemes());
        _settingsManager?.Refresh(_extensionHost.GetSettableExtensions());
    }

    // --- Cell CRUD ---

    public CellModel AddCell(string type = "code", string? language = null, string source = "")
    {
        var effectiveLanguage = language ?? ResolveDefaultLanguage(type);
        var cell = new CellModel { Type = type, Language = effectiveLanguage, Source = source };
        lock (_cellLock) { _notebook.Cells.Add(cell); }
        return cell;
    }

    public CellModel InsertCell(int index, string type = "code", string? language = null, string source = "")
    {
        var effectiveLanguage = language ?? ResolveDefaultLanguage(type);
        var cell = new CellModel { Type = type, Language = effectiveLanguage, Source = source };
        lock (_cellLock)
        {
            if (index < 0 || index > _notebook.Cells.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _notebook.Cells.Insert(index, cell);
        }
        return cell;
    }

    public bool RemoveCell(Guid cellId)
    {
        lock (_cellLock)
        {
            var cell = _notebook.Cells.FirstOrDefault(c => c.Id == cellId);
            if (cell is null) return false;
            _notebook.Cells.Remove(cell);

            // The view went with the cell, so there is nothing to tell and nothing left to route to.
            _outputChannels.CloseForCell(cellId, "the cell was deleted");
            return true;
        }
    }

    public void MoveCell(int fromIndex, int toIndex)
    {
        lock (_cellLock)
        {
            if (fromIndex < 0 || fromIndex >= _notebook.Cells.Count)
                throw new ArgumentOutOfRangeException(nameof(fromIndex));
            if (toIndex < 0 || toIndex >= _notebook.Cells.Count)
                throw new ArgumentOutOfRangeException(nameof(toIndex));
            var cell = _notebook.Cells[fromIndex];
            _notebook.Cells.RemoveAt(fromIndex);
            _notebook.Cells.Insert(toIndex, cell);
        }
    }

    public CellModel? GetCell(Guid cellId)
    {
        lock (_cellLock) { return _notebook.Cells.FirstOrDefault(c => c.Id == cellId); }
    }

    public void ClearCells()
    {
        lock (_cellLock) { _notebook.Cells.Clear(); }
    }

    public void UpdateCellSource(Guid cellId, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_cellLock)
        {
            var cell = _notebook.Cells.FirstOrDefault(c => c.Id == cellId)
                ?? throw new InvalidOperationException($"Cell {cellId} not found.");
            cell.Source = source;
        }
    }

    /// <summary>
    /// Clears all outputs from all cells in the notebook. Render-only cells (Markdown and
    /// other cell types whose "output" is the rendered form of their source rather than the
    /// result of kernel execution) are left untouched, so clearing outputs does not collapse
    /// them back to raw source. This matches Jupyter and Polyglot, where a rendered Markdown
    /// cell stays rendered when outputs are cleared.
    /// </summary>
    public void ClearAllOutputs()
    {
        lock (_cellLock)
        {
            foreach (var cell in _notebook.Cells)
            {
                if (IsRenderOnlyCell(cell))
                    continue;

                cell.Outputs.Clear();
                cell.ExecutionCount = null;
                cell.LastElapsed = null;
                cell.LastStatus = null;

                // A live output is gone with the rest, so whatever was still talking to it stops.
                _outputChannels.CloseForCell(cell.Id, "the cell's outputs were cleared");
            }
        }
    }

    /// <summary>
    /// Determines whether a cell is rendered (rather than executed by a kernel), so that its
    /// output is the rendered presentation of its source. Mirrors the kernel-vs-renderer
    /// decision in <see cref="ExecutionPipeline"/>: a registered cell type with no kernel, or
    /// (absent a cell-type match) a type that resolves to no kernel but has a matching
    /// renderer. Falls back to a Markdown check when no extension host is present.
    /// </summary>
    private bool IsRenderOnlyCell(CellModel cell)
    {
        if (_extensionHost is null)
            return string.Equals(cell.Type, "markdown", StringComparison.OrdinalIgnoreCase);

        var cellType = _extensionHost.GetCellTypes()
            .FirstOrDefault(t => string.Equals(t.CellTypeId, cell.Type, StringComparison.OrdinalIgnoreCase));
        if (cellType is not null)
            return cellType.Kernel is null;

        // No cell-type match: a cell with an explicit, resolvable language is executed.
        if (!string.IsNullOrEmpty(cell.Language) && ResolveKernel(cell.Language) is not null)
            return false;

        // Otherwise it is render-only when a renderer claims its type.
        return _extensionHost.GetRenderers()
            .Any(r => string.Equals(r.CellTypeId, cell.Type, StringComparison.OrdinalIgnoreCase));
    }

    // --- Kernel Registry ---

    public void RegisterKernel(ILanguageKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        if (_kernels.ContainsKey(kernel.LanguageId))
            throw new InvalidOperationException(
                $"A kernel is already registered for language '{kernel.LanguageId}'.");
        _kernels[kernel.LanguageId] = kernel;
    }

    public bool UnregisterKernel(string languageId)
    {
        ArgumentNullException.ThrowIfNull(languageId);
        _initializationTasks.TryRemove(languageId, out _);
        return _kernels.Remove(languageId);
    }

    public ILanguageKernel? GetKernel(string languageId)
    {
        ArgumentNullException.ThrowIfNull(languageId);
        if (_kernels.TryGetValue(languageId, out var kernel))
            return kernel;

        // Fall back to ExtensionHost-discovered kernels
        return _extensionHost?.GetKernels()
            .FirstOrDefault(k => string.Equals(k.LanguageId, languageId, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> RegisteredLanguages
    {
        get
        {
            var languages = new HashSet<string>(_kernels.Keys, StringComparer.OrdinalIgnoreCase);
            if (_extensionHost is not null)
            {
                foreach (var k in _extensionHost.GetKernels())
                    languages.Add(k.LanguageId);
            }
            return languages.ToList();
        }
    }

    /// <summary>
    /// Raised when an in-process kernel restart begins, before the old kernel is disposed.
    /// Not raised when <see cref="HostRestartHandler"/> is set: the external supervisor owns
    /// the restart lifecycle and its own progress signaling in that case.
    /// </summary>
    public event Action<string?>? OnKernelRestarting;

    /// <summary>Raised after an in-process kernel restart completes and the fresh kernel is warm.</summary>
    public event Action<string?>? OnKernelRestarted;

    /// <summary>
    /// Raised when an in-process kernel restart fails (dispose or warm-up threw). The old
    /// kernel is gone at this point, so observers should treat the kernel as unavailable
    /// until a later restart succeeds.
    /// </summary>
    public event Action<string?, Exception>? OnKernelRestartFailed;

    /// <summary>
    /// Restarts a kernel. When <see cref="HostRestartHandler"/> is set the call is
    /// delegated externally (the supervisor will respawn the process); otherwise the
    /// kernel is disposed in-process, the variable store is cleared, and the kernel
    /// is re-warmed.
    /// </summary>
    public async Task RestartKernelAsync(string? kernelId = null)
    {
        if (HostRestartHandler is { } handler)
        {
            // Told before the supervisor takes the process down. The surface holding the views
            // outlives a respawn, so a view left believing it is live would go on offering
            // controls that nothing is there to answer.
            await CloseChannelsForKernelAsync(kernelId ?? _notebook.DefaultKernelId, RestartClosedReason)
                .ConfigureAwait(false);

            await handler(kernelId).ConfigureAwait(false);
            return;
        }

        var id = kernelId ?? _notebook.DefaultKernelId
            ?? throw new InvalidOperationException(Strings.Error_NoKernelConfigured);

        var kernel = ResolveKernel(id)
            ?? throw new InvalidOperationException(string.Format(Strings.Error_NoKernelForLanguage, id));

        OnKernelRestarting?.Invoke(id);
        await CloseChannelsForKernelAsync(id, RestartClosedReason).ConfigureAwait(false);
        try
        {
            await kernel.DisposeAsync().ConfigureAwait(false);
            _initializationTasks.TryRemove(id, out _);
            _variables.Clear();

            await WarmUpKernelAsync(id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnKernelRestartFailed?.Invoke(id, ex);
            throw;
        }
        OnKernelRestarted?.Invoke(id);
    }

    /// <summary>What a view is told when the kernel behind it went away and it stayed.</summary>
    private const string RestartClosedReason = "the kernel was restarted";

    /// <summary>
    /// How long a live output is given to report its current state before a save writes what it
    /// already had. Short on purpose: the interpreter answers this between cells in well under a
    /// second, and the case the bound exists for is one that is not going to answer at all.
    /// </summary>
    private static readonly TimeSpan LiveOutputSnapshotBound = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Closes the live output channels of the cells belonging to one kernel and tells their views.
    /// Scoped by cell language rather than closing everything, because a kernel restarting says
    /// nothing about an output another kernel is keeping alive.
    /// </summary>
    private async Task CloseChannelsForKernelAsync(string? kernelId, string reason)
    {
        if (string.IsNullOrEmpty(kernelId))
            return;

        List<Guid> cellIds;
        lock (_cellLock)
        {
            cellIds = _notebook.Cells
                .Where(c => string.Equals(c.Language, kernelId, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Id)
                .ToList();
        }

        foreach (var cellId in cellIds)
            await _outputChannels.CloseForCellAsync(cellId, reason).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks every live output for its current state and writes what comes back into the notebook,
    /// so that saving records what the reader is looking at rather than what the cell drew. Called
    /// by a host on its way into serialization.
    /// </summary>
    /// <remarks>
    /// Every output is asked at once and they share one deadline, so a notebook with ten live
    /// widgets costs what one does. An output that declines, fails, or has not answered when the
    /// deadline passes keeps the content it already had, which is the state it was last known to
    /// be in and always something a file can hold. A save is never blocked and never fails here.
    /// <para>
    /// A cell's output list has no lock shared with the executions that rebuild it, so this reads
    /// each list defensively and finds its way back by channel id rather than by remembering a
    /// position. A save arriving while a cell is running therefore refreshes what it can and
    /// leaves the rest as it was, which is the same outcome as an output that did not answer.
    /// </para>
    /// </remarks>
    public async Task RefreshLiveOutputsAsync(TimeSpan? bound = null, CancellationToken ct = default)
    {
        List<CellModel> cells;
        lock (_cellLock)
        {
            cells = _notebook.Cells.ToList();
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var started = false;

        var asks = new List<(CellModel Cell, string ChannelId, Task<string?> Snapshot)>();
        foreach (var cell in cells)
        {
            foreach (var output in ReadOutputs(cell))
            {
                if (!IsLive(output))
                    continue;

                var channel = _outputChannels.Find(output.LiveChannelId!);
                if (channel is null)
                    continue;

                // Started on the first output worth asking, so a notebook with none of them does
                // not set a timer it will never use.
                if (!started)
                {
                    deadline.CancelAfter(bound ?? LiveOutputSnapshotBound);
                    started = true;
                }

                asks.Add((cell, output.LiveChannelId!,
                    OutputChannelHost.RequestSnapshotAsync(channel, deadline.Token)));
            }
        }

        if (asks.Count == 0)
            return;

        try
        {
            await Task.WhenAll(asks.Select(a => a.Snapshot)).ConfigureAwait(false);
        }
        catch
        {
            // Each ask reports its own failure and answers null. This only catches the aggregate.
        }

        foreach (var (cell, channelId, snapshot) in asks)
        {
            if (snapshot.Status != TaskStatus.RanToCompletion || snapshot.Result is not { Length: > 0 } fresh)
                continue;

            // Found again by channel rather than written back to where it was read from: a cell
            // that ran while the snapshot was in flight has a different list by now, and the
            // channel id is the one thing that still says which output this answer belongs to.
            try
            {
                for (var i = 0; i < cell.Outputs.Count; i++)
                {
                    if (!string.Equals(cell.Outputs[i].LiveChannelId, channelId, StringComparison.Ordinal))
                        continue;

                    cell.Outputs[i] = cell.Outputs[i] with { Content = fresh };
                    break;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // The cell was rebuilt underneath this. The output being refreshed went with it,
                // so there is nothing left to write the answer into.
            }
        }

        static bool IsLive(CellOutput output) => !string.IsNullOrEmpty(output.LiveChannelId);
    }

    /// <summary>
    /// A cell's outputs as they stood, or nothing when the cell was being rebuilt as they were
    /// read. Copying is what makes the read safe to walk: the list belongs to whatever execution
    /// last touched it, and nothing here holds a lock that execution takes.
    /// </summary>
    private static IReadOnlyList<CellOutput> ReadOutputs(CellModel cell)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { return cell.Outputs.ToArray(); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // Mutated mid-copy. Worth one more go, because the window is a single execution
                // writing one output and the second read usually lands between two of them.
            }
        }

        return Array.Empty<CellOutput>();
    }

    /// <summary>
    /// Disposes and re-initializes all active kernels and clears the variable store.
    /// Used by <see cref="ExecuteAllAsync"/> to ensure a clean slate without visible
    /// kernel-restart feedback in the UI.
    /// </summary>
    private async Task ResetAllKernelsAsync()
    {
        // Every kernel goes, so every live output loses whatever was behind it.
        await _outputChannels.CloseAllAsync(RestartClosedReason).ConfigureAwait(false);

        // Preserve parameter values across the reset so that explicitly
        // set parameters (e.g. via --param or the Parameters cell) survive.
        var parameterNames = _notebook.Parameters?.Keys;
        List<(string Name, object Value)>? savedParams = null;

        if (parameterNames is { Count: > 0 })
        {
            savedParams = new List<(string, object)>();
            foreach (var name in parameterNames)
            {
                if (_variables.TryGet<object>(name, out var value) && value is not null)
                    savedParams.Add((name, value));
            }
        }

        foreach (var kernel in _kernels.Values)
        {
            await kernel.DisposeAsync().ConfigureAwait(false);
        }

        _initializationTasks.Clear();
        _variables.Clear();

        // Restore parameter values
        if (savedParams is not null)
        {
            foreach (var (name, value) in savedParams)
                _variables.Set(name, value);
        }
    }

    // --- Execution ---

    /// <summary>
    /// Raised when a cell is about to begin execution. Fires once per cell for
    /// both single-cell and Run All paths. Observers (UI, variable explorer,
    /// binding router, host RPC bridge) use this to drive per-cell state
    /// transitions without polling.
    /// </summary>
    public event Action<Guid>? OnCellExecuting;

    /// <summary>
    /// Raised after a cell finishes execution and its <see cref="CellModel"/>
    /// execution metadata has been stamped (ExecutionCount, LastElapsed,
    /// LastStatus). Observers may read those fields directly from the cell.
    /// </summary>
    public event Action<Guid>? OnCellExecuted;

    /// <summary>
    /// Raised when a cell receives output during execution before the cell has
    /// completed. Remote front-ends use this to refresh running cell output.
    /// </summary>
    public event Action<Guid>? OnCellOutputUpdated;

    /// <summary>
    /// Optional front-end hook used by kernels that need a single line of user
    /// input during execution, such as PowerShell Read-Host.
    /// </summary>
    public Func<Guid, string, bool, CancellationToken, Task<string?>>? InputRequester { get; set; }

    public async Task<ExecutionResult> ExecuteCellAsync(Guid cellId, CancellationToken ct = default)
    {
        // Ensure parameter defaults are in the variable store before any cell runs
        EnsureParametersInjected();

        CellModel cell;
        lock (_cellLock)
        {
            cell = _notebook.Cells.FirstOrDefault(c => c.Id == cellId)
                ?? throw new InvalidOperationException($"Cell {cellId} not found.");
        }

        IncrementExecutionCount(cellId);

        OnCellExecuting?.Invoke(cellId);

        // Yield before starting the pipeline so in-process observers (e.g. the
        // Blazor Server renderer) get a chance to paint the "running" state before
        // kernel work may run synchronously enough to starve the dispatcher.
        await Task.Yield();

        var pipeline = BuildPipeline();
        var result = await pipeline.ExecuteAsync(cell, ct).ConfigureAwait(false);

        cell.ExecutionCount = result.ExecutionCount;
        cell.LastElapsed = result.Elapsed;
        cell.LastStatus = result.Status.ToString();

        OnCellExecuted?.Invoke(cellId);

        // Symmetric yield so in-process observers can paint the cell's final
        // state (outputs, exec count, cleared spinner) before the next cell
        // starts in a Run All batch.
        await Task.Yield();

        return result;
    }

    public async Task<IReadOnlyList<ExecutionResult>> ExecuteAllAsync(CancellationToken ct = default)
    {
        // Reset all kernels to a clean state so Run All behaves as if
        // the notebook is being executed from scratch. Without this,
        // stale variables from previous runs can leak into the new run.
        await ResetAllKernelsAsync().ConfigureAwait(false);

        // Inject parameters and validate required ones before execution
        EnsureParametersInjected();
        var paramError = ValidateRequiredParameters();

        List<Guid> cellIds;
        lock (_cellLock)
        {
            cellIds = _notebook.Cells.Select(c => c.Id).ToList();
        }

        if (paramError is not null)
        {
            // Find the parameters cell to attach the error
            CellModel? errorCell;
            lock (_cellLock)
            {
                errorCell = _notebook.Cells.FirstOrDefault(c =>
                    string.Equals(c.Type, "parameters", StringComparison.OrdinalIgnoreCase))
                    ?? _notebook.Cells.FirstOrDefault();
            }

            if (errorCell is not null)
            {
                // Re-render the parameters cell so the form is preserved,
                // then append the validation error below the form.
                var pipeline = BuildPipeline();
                await pipeline.ExecuteAsync(errorCell, ct).ConfigureAwait(false);

                errorCell.Outputs.Add(new CellOutput("text/plain",
                    paramError,
                    IsError: true, ErrorName: "ParameterValidationError"));
            }

            return new List<ExecutionResult>
            {
                ExecutionResult.Failed(
                    errorCell?.Id ?? Guid.Empty, 0, TimeSpan.Zero,
                    new InvalidOperationException(paramError))
            };
        }

        var results = new List<ExecutionResult>();
        foreach (var id in cellIds)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ExecuteCellAsync(id, ct).ConfigureAwait(false));
        }
        return results;
    }

    /// <summary>
    /// Renders every cell whose type is render-only (no kernel) and declares transient
    /// outputs (<see cref="ICellType.PersistsOutputs"/> is <c>false</c>), when the cell has
    /// source but no outputs yet. Hosts call this when a notebook opens so markup cells
    /// (e.g. Markdown) display rendered on first paint instead of as raw source. The render
    /// pipeline runs directly: execution counts are not incremented and no execution events
    /// fire, so rendering at open cannot mark the notebook as edited.
    /// </summary>
    public async Task RenderTransientCellsAsync(CancellationToken ct = default)
    {
        var cellTypes = ExtensionHostContext.GetCellTypes();
        if (cellTypes.Count == 0)
            return;

        List<CellModel> cells;
        lock (_cellLock) { cells = _notebook.Cells.ToList(); }

        ExecutionPipeline? pipeline = null;
        foreach (var cell in cells)
        {
            ct.ThrowIfCancellationRequested();

            if (cell.Outputs.Count > 0 || string.IsNullOrWhiteSpace(cell.Source))
                continue;

            var cellType = cellTypes.FirstOrDefault(t =>
                string.Equals(t.CellTypeId, cell.Type, StringComparison.OrdinalIgnoreCase));
            if (cellType is null || cellType.Kernel is not null || cellType.PersistsOutputs)
                continue;

            pipeline ??= BuildPipeline();
            await pipeline.ExecuteAsync(cell, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ensures parameter defaults are injected into the variable store.
    /// Safe to call multiple times; only injects values not already present.
    /// </summary>
    private void EnsureParametersInjected()
    {
        var parameters = _notebook.Parameters;
        if (parameters is null || parameters.Count == 0)
            return;

        foreach (var (name, def) in parameters)
        {
            if (_variables.TryGet<object>(name, out var existing) && existing is not null)
                continue;

            if (def.Default is not null)
            {
                // Ensure the value is the correct CLR type. Defaults deserialized from
                // JSON may be strings or JsonElements rather than typed objects.
                var typed = CoerceParameterValue(def.Default, def.Type);
                _variables.Set(name, typed);
            }
        }
    }

    /// <summary>
    /// Coerces a parameter default value to the correct CLR type. Values loaded from
    /// JSON may arrive as strings or JsonElements; this ensures they match the declared type.
    /// </summary>
    private static object CoerceParameterValue(object value, string typeId)
    {
        // Already the expected CLR type?
        if (typeId == "int" && (value is long || value is int))
            return value is int i ? (long)i : value;
        if (typeId == "float" && value is double)
            return value;
        if (typeId == "bool" && value is bool)
            return value;
        if (typeId == "date" && value is DateOnly)
            return value;
        if (typeId == "datetime" && value is DateTimeOffset)
            return value;
        if (typeId == "string" && value is string)
            return value;

        // Try parsing from string representation
        var str = value.ToString() ?? "";
        if (Parameters.ParameterValueParser.TryParse(typeId, str, out var parsed, out _) && parsed is not null)
            return parsed;

        // Fall back to original value
        return value;
    }

    /// <summary>
    /// Validates that all required parameters have values. Returns an error message
    /// listing missing parameters, or null if all are satisfied.
    /// </summary>
    private string? ValidateRequiredParameters()
    {
        var parameters = _notebook.Parameters;
        if (parameters is null || parameters.Count == 0)
            return null;

        var missing = new List<(string Name, string Type, string? Description)>();

        foreach (var (name, def) in parameters)
        {
            if (!def.Required) continue;

            // Check if the variable store has a meaningful value
            if (_variables.TryGet<object>(name, out var existing) && existing is not null
                && !IsEmptyParameterValue(existing, def.Type))
                continue;

            // Check if there is a meaningful default
            if (def.Default is not null && !IsEmptyParameterValue(def.Default, def.Type))
                continue;

            missing.Add((name, def.Type, def.Description));
        }

        if (missing.Count == 0)
            return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Missing required notebook parameters:");
        sb.AppendLine();
        foreach (var (name, type, desc) in missing)
        {
            sb.Append($"  {name} ({type})");
            if (!string.IsNullOrEmpty(desc))
                sb.Append($"  {desc}");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.Append("Set values in the Parameters cell or supply them via --param.");
        return sb.ToString();
    }

    /// <summary>
    /// Returns true if the value is considered "empty" for a required parameter of the given type.
    /// Empty strings, CLR-default dates, and CLR-default datetimes all count as missing.
    /// </summary>
    private static bool IsEmptyParameterValue(object value, string typeId) => typeId switch
    {
        "string" => value is string s && string.IsNullOrWhiteSpace(s),
        "date" => value is DateOnly d && d == default,
        "datetime" => value is DateTimeOffset dto && dto == default,
        _ => false
    };

    public async Task<ExecutionResult> ExecuteCodeAsync(string code, string? language = null, CancellationToken ct = default)
    {
        var (result, _) = await ExecuteCodeCoreAsync(code, language, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Executes arbitrary code and returns both the execution result and the outputs it produced.
    /// The outputs accumulate on a transient cell that is never added to the notebook.
    /// </summary>
    public async Task<IReadOnlyList<CellOutput>> ExecuteCodeCaptureOutputsAsync(
        string code, string? language = null, CancellationToken ct = default)
    {
        var (_, outputs) = await ExecuteCodeCoreAsync(code, language, ct).ConfigureAwait(false);
        return outputs;
    }

    private async Task<(ExecutionResult Result, IReadOnlyList<CellOutput> Outputs)> ExecuteCodeCoreAsync(
        string code, string? language, CancellationToken ct)
    {
        var transientCell = new CellModel
        {
            Type = "code",
            Language = language ?? _notebook.DefaultKernelId,
            Source = code
        };
        var pipeline = BuildPipeline();
        var result = await pipeline.ExecuteAsync(transientCell, ct).ConfigureAwait(false);
        List<CellOutput> outputs;
        lock (transientCell.Outputs)
        {
            outputs = transientCell.Outputs.ToList();
        }
        return (result, outputs);
    }

    // --- Lifecycle ---

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop collecting first: a fault raised from here on has no cell of this notebook's left
        // to be reported against.
        _backgroundFaultRegistration.Dispose();

        // Before the kernels, which own the channels: closing first means a kernel tearing down
        // finds every channel of its own already dead rather than posting into one.
        await _outputChannels.DisposeAsync().ConfigureAwait(false);

        foreach (var kernel in _kernels.Values)
        {
            await kernel.DisposeAsync().ConfigureAwait(false);
        }
        _kernels.Clear();
        _initializationTasks.Clear();

        if (_extensionHost is not null)
            await _extensionHost.DisposeAsync().ConfigureAwait(false);
    }

    // --- Private helpers ---

    private void IncrementExecutionCount(Guid cellId)
    {
        if (_executionCounts.TryGetValue(cellId, out var count))
            _executionCounts[cellId] = count + 1;
        else
            _executionCounts[cellId] = 1;
    }

    private int GetExecutionCount(Guid cellId)
    {
        return _executionCounts.TryGetValue(cellId, out var count) ? count : 0;
    }

    private string? ResolveLanguageId(Guid cellId)
    {
        CellModel? cell;
        lock (_cellLock) { cell = _notebook.Cells.FirstOrDefault(c => c.Id == cellId); }
        return cell?.Language ?? _notebook.DefaultKernelId;
    }

    /// <summary>
    /// Eagerly initializes a kernel so IntelliSense is available before the first execution.
    /// Safe to call concurrently; concurrent calls for the same language share a single init task.
    /// </summary>
    public Task WarmUpKernelAsync(string languageId)
    {
        var kernel = ResolveKernel(languageId);
        if (kernel is null) return Task.CompletedTask;
        return GetOrCreateInitTask(languageId, kernel);
    }

    private Task EnsureInitialized(ILanguageKernel kernel)
    {
        return GetOrCreateInitTask(kernel.LanguageId, kernel);
    }

    private Task GetOrCreateInitTask(string languageId, ILanguageKernel kernel)
    {
        return _initializationTasks.GetOrAdd(
            languageId,
            _ => new Lazy<Task>(
                () => kernel.InitializeAsync(),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private ILanguageKernel? ResolveKernel(string languageId)
    {
        if (_kernels.TryGetValue(languageId, out var k))
            return k;

        if (_extensionHost is null)
            return null;

        var kernel = _extensionHost.GetKernels()
            .FirstOrDefault(ek => string.Equals(ek.LanguageId, languageId, StringComparison.OrdinalIgnoreCase));
        if (kernel is not null)
            return kernel;

        // Kernels contributed through a cell type (e.g. the SQL cell type) are not registered
        // as standalone kernel extensions, so they are invisible to the lookups above. Code
        // routed by language alone (ExecuteCodeAsync, #!import) must still reach the same
        // kernel instance a typed cell would use, otherwise it falls back to the notebook's
        // default kernel and executes in the wrong language.
        return _extensionHost.GetCellTypes()
            .Select(t => t.Kernel)
            .FirstOrDefault(tk => tk is not null &&
                string.Equals(tk.LanguageId, languageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the language a newly created cell of the given type should use when no explicit
    /// language is supplied. A registered cell type contributes its kernel language (e.g. an "sql"
    /// cell type yields "sql"); render-only types (no kernel, or a matching renderer) get no language;
    /// anything else falls back to the notebook's default kernel.
    /// </summary>
    private string? ResolveDefaultLanguage(string type)
    {
        if (_extensionHost is null)
            return string.Equals(type, "markdown", StringComparison.OrdinalIgnoreCase)
                ? null
                : _notebook.DefaultKernelId;

        var cellType = _extensionHost.GetCellTypes()
            .FirstOrDefault(t => string.Equals(t.CellTypeId, type, StringComparison.OrdinalIgnoreCase));

        var language = cellType?.Kernel?.LanguageId;
        if (language is null)
        {
            var hasRenderer = _extensionHost.GetRenderers()
                .Any(r => string.Equals(r.CellTypeId, type, StringComparison.OrdinalIgnoreCase));
            if (!hasRenderer)
                language = _notebook.DefaultKernelId;
        }

        return language;
    }

    private IMagicCommand? ResolveMagicCommand(string name)
    {
        return _extensionHost?.GetMagicCommands()
            .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private ExecutionPipeline BuildPipeline()
    {
        return new ExecutionPipeline(
            _variables,
            ThemeContext,
            LayoutCapabilities,
            ExtensionHostContext,
            _metadata,
            _notebookOps,
            ResolveKernel,
            EnsureInitialized,
            ResolveLanguageId,
            GetExecutionCount,
            ResolveMagicCommand,
            id => OnCellOutputUpdated?.Invoke(id),
            InputRequester,
            _backgroundFaults,
            _outputChannels);
    }
}
