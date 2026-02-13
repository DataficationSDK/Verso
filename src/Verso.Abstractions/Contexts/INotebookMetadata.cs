namespace Verso.Abstractions;

/// <summary>
/// Provides read-only access to notebook-level metadata such as title, default kernel, and file path.
/// </summary>
public interface INotebookMetadata
{
    /// <summary>
    /// Gets the notebook title, or <c>null</c> if no title is set.
    /// </summary>
    string? Title { get; }

    /// <summary>
    /// Gets the identifier of the default language kernel for new cells, or <c>null</c> if not specified.
    /// </summary>
    string? DefaultKernelId { get; }

    /// <summary>
    /// Gets the file path of the notebook on disk, or <c>null</c> if the notebook has not been saved.
    /// </summary>
    string? FilePath { get; }
}
