using Verso.Abstractions;
using Verso.Contexts;
using Verso.Execution;
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
    private readonly StubThemeContext _theme = new();
    private readonly StubExtensionHostContext _extensionHost;
    private bool _disposed;

    public Scaffold() : this(new NotebookModel()) { }

    public Scaffold(NotebookModel notebook)
    {
        _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
        _metadata = new NotebookMetadataContext(_notebook);
        _extensionHost = new StubExtensionHostContext(() => _kernels.Values.ToList());
        LayoutCapabilities = LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete |
                             LayoutCapabilities.CellReorder | LayoutCapabilities.CellEdit |
                             LayoutCapabilities.CellResize | LayoutCapabilities.CellExecute |
                             LayoutCapabilities.MultiSelect;
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
    public IThemeContext ThemeContext => _theme;
    public LayoutCapabilities LayoutCapabilities { get; set; }
    public IExtensionHostContext ExtensionHostContext => _extensionHost;

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
        return _kernels.TryGetValue(languageId, out var kernel) ? kernel : null;
    }

    public IReadOnlyList<string> RegisteredLanguages => _kernels.Keys.ToList();

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

    private ExecutionPipeline BuildPipeline()
    {
        return new ExecutionPipeline(
            _variables,
            _theme,
            LayoutCapabilities,
            _extensionHost,
            _metadata,
            languageId => _kernels.TryGetValue(languageId, out var k) ? k : null,
            EnsureInitialized,
            ResolveLanguageId,
            GetExecutionCount);
    }
}
