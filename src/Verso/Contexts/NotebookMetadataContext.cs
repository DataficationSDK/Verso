using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// <see cref="INotebookMetadata"/> implementation backed by a <see cref="NotebookModel"/>.
/// </summary>
public sealed class NotebookMetadataContext : INotebookMetadata
{
    private readonly NotebookModel _notebook;

    public NotebookMetadataContext(NotebookModel notebook)
    {
        _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
    }

    /// <inheritdoc />
    public string? Title => _notebook.Title;

    /// <inheritdoc />
    public string? DefaultKernelId => _notebook.DefaultKernelId;

    /// <inheritdoc />
    public string? FilePath => null;
}
