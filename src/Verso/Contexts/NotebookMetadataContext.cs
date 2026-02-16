using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// <see cref="INotebookMetadata"/> implementation backed by a <see cref="NotebookModel"/>.
/// </summary>
public sealed class NotebookMetadataContext : INotebookMetadata
{
    private readonly NotebookModel _notebook;
    private string? _filePath;

    public NotebookMetadataContext(NotebookModel notebook, string? filePath = null)
    {
        _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
        _filePath = filePath;
    }

    /// <inheritdoc />
    public string? Title => _notebook.Title;

    /// <inheritdoc />
    public string? DefaultKernelId => _notebook.DefaultKernelId;

    /// <inheritdoc />
    public string? FilePath { get => _filePath; internal set => _filePath = value; }
}
