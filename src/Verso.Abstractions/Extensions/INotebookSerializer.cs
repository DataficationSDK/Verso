namespace Verso.Abstractions;

/// <summary>
/// Serializes and deserializes notebooks to and from a specific file format (e.g. .ipynb, .dib, .vnb).
/// The host selects the appropriate serializer based on <see cref="FileExtensions"/>.
/// </summary>
public interface INotebookSerializer : IExtension
{
    /// <summary>
    /// Unique identifier for the serialization format (e.g. "jupyter", "verso-native").
    /// </summary>
    string FormatId { get; }

    /// <summary>
    /// File extensions this serializer handles (e.g. ".ipynb", ".vnb"), including the leading dot.
    /// </summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Serializes a notebook model to its string representation in this format.
    /// </summary>
    /// <param name="notebook">The notebook model to serialize.</param>
    /// <returns>The serialized string content.</returns>
    Task<string> SerializeAsync(NotebookModel notebook);

    /// <summary>
    /// Deserializes the string content of a notebook file into a notebook model.
    /// </summary>
    /// <param name="content">The raw file content to deserialize.</param>
    /// <returns>The deserialized notebook model.</returns>
    Task<NotebookModel> DeserializeAsync(string content);

    /// <summary>
    /// Determines whether this serializer can import the file at the given path,
    /// typically by inspecting the file extension or probing the content.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the file.</param>
    /// <returns><c>true</c> if this serializer can import the file; otherwise <c>false</c>.</returns>
    bool CanImport(string filePath);
}
