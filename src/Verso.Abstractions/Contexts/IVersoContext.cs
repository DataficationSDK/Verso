namespace Verso.Abstractions;

/// <summary>
/// Base context provided to all Verso extension operations, exposing shared services and capabilities.
/// </summary>
public interface IVersoContext
{
    /// <summary>
    /// Gets the shared variable store for exchanging data between kernels.
    /// </summary>
    IVariableStore Variables { get; }

    /// <summary>
    /// Gets the cancellation token that signals when the current operation should be aborted.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Writes a cell output to the notebook output stream.
    /// </summary>
    /// <param name="output">The cell output to write.</param>
    /// <returns>A task that completes when the output has been written.</returns>
    Task WriteOutputAsync(CellOutput output);

    /// <summary>
    /// Gets the active theme context for resolving colors, fonts, and spacing.
    /// </summary>
    IThemeContext Theme { get; }

    /// <summary>
    /// Gets the layout capabilities supported by the current rendering surface.
    /// </summary>
    LayoutCapabilities LayoutCapabilities { get; }

    /// <summary>
    /// Gets the extension host context for querying loaded extensions by category.
    /// </summary>
    IExtensionHostContext ExtensionHost { get; }

    /// <summary>
    /// Gets the read-only metadata for the current notebook.
    /// </summary>
    INotebookMetadata NotebookMetadata { get; }

    /// <summary>
    /// Gets the notebook operations interface for executing cells, managing outputs, and mutating the cell collection.
    /// </summary>
    INotebookOperations Notebook { get; }

    /// <summary>
    /// Requests that the host deliver a file download to the user.
    /// </summary>
    /// <param name="fileName">The suggested file name for the download.</param>
    /// <param name="contentType">The MIME content type of the file.</param>
    /// <param name="data">The file contents as a byte array.</param>
    /// <returns>A task that completes when the download has been initiated.</returns>
    Task RequestFileDownloadAsync(string fileName, string contentType, byte[] data)
    {
        throw new NotSupportedException("File download is not supported by this host.");
    }
}
