namespace Verso.Abstractions;

/// <summary>
/// Extension capability for post-processing notebooks after deserialization and before serialization.
/// Implementations can transform cells, inject metadata, or migrate legacy formats.
/// </summary>
public interface INotebookPostProcessor : IExtension
{
    /// <summary>
    /// Execution priority. Lower values run first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Determines whether this post-processor should run for the given file and format.
    /// </summary>
    /// <param name="filePath">The file path being opened/saved, or <c>null</c> if not available.</param>
    /// <param name="formatId">The serialization format identifier (e.g. "jupyter", "verso-native").</param>
    /// <returns><c>true</c> if this post-processor should participate.</returns>
    bool CanProcess(string? filePath, string formatId);

    /// <summary>
    /// Transforms a notebook after deserialization (on open/import).
    /// </summary>
    /// <param name="notebook">The deserialized notebook model.</param>
    /// <param name="filePath">The source file path, or <c>null</c> if not available.</param>
    /// <returns>The (possibly modified) notebook model.</returns>
    Task<NotebookModel> PostDeserializeAsync(NotebookModel notebook, string? filePath);

    /// <summary>
    /// Transforms a notebook before serialization (on save/export).
    /// </summary>
    /// <param name="notebook">The notebook model about to be serialized.</param>
    /// <param name="filePath">The target file path, or <c>null</c> if not available.</param>
    /// <returns>The (possibly modified) notebook model.</returns>
    Task<NotebookModel> PreSerializeAsync(NotebookModel notebook, string? filePath);
}
