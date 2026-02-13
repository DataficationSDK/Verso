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
}
