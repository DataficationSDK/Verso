using System.Collections.Concurrent;
using Verso.Abstractions;
using Verso.Contexts;
using Verso.Execution;
using Verso.Extensions;
using Verso.Stubs;

namespace Verso;

/// <summary>
/// Core orchestrator — cell CRUD, kernel registry, execution dispatch, shared state, and subsystem hooks.
/// </summary>
public sealed class Scaffold : IAsyncDisposable
{
    private readonly NotebookModel _notebook;
    private readonly object _cellLock = new();
    private readonly Dictionary<string, ILanguageKernel> _kernels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _initializationTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, int> _executionCounts = new();
    private readonly VariableStore _variables = new();
    private readonly NotebookMetadataContext _metadata;
    private readonly StubExtensionHostContext _stubExtensionHost;
    private readonly ExtensionHost? _extensionHost;
    private ThemeEngine? _themeEngine;
    private LayoutManager? _layoutManager;
    private SettingsManager? _settingsManager;
    private readonly NotebookOperations _notebookOps;
    private bool _disposed;

    public Scaffold() : this(new NotebookModel()) { }

    public Scaffold(NotebookModel notebook) : this(notebook, extensionHost: null) { }

    public Scaffold(NotebookModel notebook, ExtensionHost? extensionHost, string? filePath = null)
    {
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
    /// Updates the notebook file path used for resolving relative paths (e.g. in <c>#!import</c>).
    /// This is called after construction when the file path is not available at open time.
    /// </summary>
    public void SetFilePath(string? filePath)
    {
        _metadata.FilePath = filePath;
    }

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
        _layoutManager = new LayoutManager(layouts, _notebook.ActiveLayoutId);

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
    /// Clears all outputs from all cells in the notebook.
    /// </summary>
    public void ClearAllOutputs()
    {
        lock (_cellLock)
        {
            foreach (var cell in _notebook.Cells)
            {
                cell.Outputs.Clear();
                cell.ExecutionCount = null;
                cell.LastElapsed = null;
                cell.LastStatus = null;
            }
        }
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
    /// Restarts a kernel: disposes it, removes from initialized set, clears variables, and re-initializes.
    /// </summary>
    public async Task RestartKernelAsync(string? kernelId = null)
    {
        var id = kernelId ?? _notebook.DefaultKernelId
            ?? throw new InvalidOperationException("No kernel ID specified and no default kernel is configured.");

        var kernel = ResolveKernel(id)
            ?? throw new InvalidOperationException($"No kernel registered for language '{id}'.");

        await kernel.DisposeAsync().ConfigureAwait(false);
        _initializationTasks.TryRemove(id, out _);
        _variables.Clear();

        await WarmUpKernelAsync(id).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes and re-initializes all active kernels and clears the variable store.
    /// Used by <see cref="ExecuteAllAsync"/> to ensure a clean slate without visible
    /// kernel-restart feedback in the UI.
    /// </summary>
    private async Task ResetAllKernelsAsync()
    {
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
        var transientCell = new CellModel
        {
            Type = "code",
            Language = language ?? _notebook.DefaultKernelId,
            Source = code
        };
        var pipeline = BuildPipeline();
        return await pipeline.ExecuteAsync(transientCell, ct).ConfigureAwait(false);
    }

    // --- Lifecycle ---

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

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
    /// Safe to call concurrently — concurrent calls for the same language share a single init task.
    /// </summary>
    public Task WarmUpKernelAsync(string languageId)
    {
        var kernel = ResolveKernel(languageId);
        if (kernel is null) return Task.CompletedTask;
        return _initializationTasks.GetOrAdd(languageId, _ => kernel.InitializeAsync());
    }

    private Task EnsureInitialized(ILanguageKernel kernel)
    {
        return _initializationTasks.GetOrAdd(kernel.LanguageId, _ => kernel.InitializeAsync());
    }

    private ILanguageKernel? ResolveKernel(string languageId)
    {
        if (_kernels.TryGetValue(languageId, out var k))
            return k;

        return _extensionHost?.GetKernels()
            .FirstOrDefault(ek => string.Equals(ek.LanguageId, languageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the notebook's default kernel language for executable cell types, or <c>null</c> for
    /// types that don't need a language (e.g. markdown).
    /// </summary>
    private string? ResolveDefaultLanguage(string type)
    {
        if (string.Equals(type, "markdown", StringComparison.OrdinalIgnoreCase))
            return null;
        return _notebook.DefaultKernelId;
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
            ResolveMagicCommand);
    }
}
