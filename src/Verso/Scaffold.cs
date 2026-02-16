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
    private readonly HashSet<string> _initializedKernels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, int> _executionCounts = new();
    private readonly VariableStore _variables = new();
    private readonly NotebookMetadataContext _metadata;
    private readonly StubExtensionHostContext _stubExtensionHost;
    private readonly ExtensionHost? _extensionHost;
    private ThemeEngine? _themeEngine;
    private LayoutManager? _layoutManager;
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
    }

    // --- Cell CRUD ---

    public CellModel AddCell(string type = "code", string? language = null, string source = "")
    {
        var cell = new CellModel { Type = type, Language = language, Source = source };
        lock (_cellLock) { _notebook.Cells.Add(cell); }
        return cell;
    }

    public CellModel InsertCell(int index, string type = "code", string? language = null, string source = "")
    {
        var cell = new CellModel { Type = type, Language = language, Source = source };
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
                cell.Outputs.Clear();
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
        _initializedKernels.Remove(languageId);
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
        _initializedKernels.Remove(id);
        _variables.Clear();

        await EnsureInitialized(kernel).ConfigureAwait(false);
    }

    // --- Execution ---

    public async Task<ExecutionResult> ExecuteCellAsync(Guid cellId, CancellationToken ct = default)
    {
        CellModel cell;
        lock (_cellLock)
        {
            cell = _notebook.Cells.FirstOrDefault(c => c.Id == cellId)
                ?? throw new InvalidOperationException($"Cell {cellId} not found.");
        }

        IncrementExecutionCount(cellId);

        var pipeline = BuildPipeline();
        return await pipeline.ExecuteAsync(cell, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExecutionResult>> ExecuteAllAsync(CancellationToken ct = default)
    {
        List<Guid> cellIds;
        lock (_cellLock)
        {
            cellIds = _notebook.Cells.Select(c => c.Id).ToList();
        }

        var results = new List<ExecutionResult>();
        foreach (var id in cellIds)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ExecuteCellAsync(id, ct).ConfigureAwait(false));
        }
        return results;
    }

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
        _initializedKernels.Clear();

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

    private async Task EnsureInitialized(ILanguageKernel kernel)
    {
        if (_initializedKernels.Contains(kernel.LanguageId)) return;
        await kernel.InitializeAsync().ConfigureAwait(false);
        _initializedKernels.Add(kernel.LanguageId);
    }

    private ILanguageKernel? ResolveKernel(string languageId)
    {
        if (_kernels.TryGetValue(languageId, out var k))
            return k;

        return _extensionHost?.GetKernels()
            .FirstOrDefault(ek => string.Equals(ek.LanguageId, languageId, StringComparison.OrdinalIgnoreCase));
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
