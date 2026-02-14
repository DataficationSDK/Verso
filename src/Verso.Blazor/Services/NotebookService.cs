using Verso.Abstractions;
using Verso.Execution;
using Verso.Extensions;
using Verso.Serializers;

namespace Verso.Blazor.Services;

/// <summary>
/// Scoped service that manages the Scaffold and ExtensionHost lifecycle for a Blazor circuit.
/// Single integration point between Blazor components and the Verso engine.
/// </summary>
public sealed class NotebookService : IAsyncDisposable
{
    private Scaffold? _scaffold;
    private ExtensionHost? _extensionHost;
    private string? _filePath;

    public Scaffold? Scaffold => _scaffold;
    public ExtensionHost? ExtensionHost => _extensionHost;
    public bool IsLoaded => _scaffold is not null;
    public string? FilePath => _filePath;

    /// <summary>Raised after a cell finishes execution.</summary>
    public event Action? OnCellExecuted;

    /// <summary>Raised when the notebook structure changes (add, remove, move, new, open).</summary>
    public event Action? OnNotebookChanged;

    /// <summary>Open and deserialize a notebook file (.verso, .ipynb, etc.).</summary>
    public async Task OpenAsync(string filePath)
    {
        await DisposeCurrentAsync();

        _extensionHost = new ExtensionHost();
        await _extensionHost.LoadBuiltInExtensionsAsync();

        var content = await File.ReadAllTextAsync(filePath);

        // Select the right serializer based on file extension
        var serializer = _extensionHost.GetSerializers()
            .FirstOrDefault(s => s.CanImport(filePath))
            ?? (INotebookSerializer)new VersoSerializer();

        var notebook = await serializer.DeserializeAsync(content);

        _scaffold = new Scaffold(notebook, _extensionHost);
        _scaffold.InitializeSubsystems();
        _filePath = filePath;

        OnNotebookChanged?.Invoke();
    }

    /// <summary>Serialize current notebook to a .verso file.</summary>
    public async Task SaveAsync(string filePath)
    {
        if (_scaffold is null) return;

        _scaffold.Notebook.Modified = DateTimeOffset.UtcNow;
        var serializer = new VersoSerializer();
        var json = await serializer.SerializeAsync(_scaffold.Notebook);
        await File.WriteAllTextAsync(filePath, json);
        _filePath = filePath;
    }

    /// <summary>Create a new empty notebook with one default code cell.</summary>
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
        _scaffold.AddCell("code", "csharp");
        _filePath = null;

        OnNotebookChanged?.Invoke();
    }

    /// <summary>Execute a single cell by ID. The engine routes to kernel or renderer per cell type.</summary>
    public async Task<ExecutionResult> ExecuteCellAsync(Guid cellId)
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var result = await _scaffold.ExecuteCellAsync(cellId);
        OnCellExecuted?.Invoke();
        return result;
    }

    /// <summary>Execute all cells in order.</summary>
    public async Task<IReadOnlyList<ExecutionResult>> ExecuteAllAsync()
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        var results = await _scaffold.ExecuteAllAsync();
        OnCellExecuted?.Invoke();
        return results;
    }

    /// <summary>Add a new cell at the end.</summary>
    public CellModel AddCell(string type = "code", string? language = null)
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is loaded.");

        // Check ICellType registry to determine if this type has a kernel.
        // Non-executable cell types (no kernel) don't get a language assigned.
        var effectiveLanguage = language;
        if (effectiveLanguage is null)
        {
            var cellType = _extensionHost?.GetCellTypes()
                .FirstOrDefault(t => string.Equals(t.CellTypeId, type, StringComparison.OrdinalIgnoreCase));

            if (cellType is not null)
                effectiveLanguage = cellType.Kernel?.LanguageId;
            else if (HasKernelOrNoRenderer(type))
                effectiveLanguage = _scaffold.DefaultKernelId ?? "csharp";
        }

        var cell = _scaffold.AddCell(type, effectiveLanguage);
        OnNotebookChanged?.Invoke();
        return cell;
    }

    /// <summary>
    /// Returns true if the cell type has no matching renderer (assumed to be a code cell
    /// that should get a default language).
    /// </summary>
    private bool HasKernelOrNoRenderer(string type)
    {
        var hasRenderer = _extensionHost?.GetRenderers()
            .Any(r => string.Equals(r.CellTypeId, type, StringComparison.OrdinalIgnoreCase)) ?? false;
        return !hasRenderer;
    }

    /// <summary>Remove a cell by ID.</summary>
    public bool RemoveCell(Guid cellId)
    {
        if (_scaffold is null) return false;

        var result = _scaffold.RemoveCell(cellId);
        if (result) OnNotebookChanged?.Invoke();
        return result;
    }

    /// <summary>Move a cell from one position to another.</summary>
    public void MoveCellAsync(int fromIndex, int toIndex)
    {
        if (_scaffold is null) return;

        _scaffold.MoveCell(fromIndex, toIndex);
        OnNotebookChanged?.Invoke();
    }

    /// <summary>Update the source of a cell.</summary>
    public void UpdateCellSource(Guid cellId, string source)
    {
        _scaffold?.UpdateCellSource(cellId, source);
    }

    /// <summary>Clear all cell outputs.</summary>
    public void ClearAllOutputs()
    {
        _scaffold?.ClearAllOutputs();
        OnCellExecuted?.Invoke();
    }

    /// <summary>Restart the active kernel.</summary>
    public async Task RestartKernelAsync()
    {
        if (_scaffold is null) return;
        await _scaffold.RestartKernelAsync();
    }

    private async Task DisposeCurrentAsync()
    {
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
