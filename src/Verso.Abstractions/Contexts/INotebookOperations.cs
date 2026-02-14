namespace Verso.Abstractions;

/// <summary>
/// Provides centralized notebook-level operations to all extensions.
/// Accessed via <see cref="IVersoContext.Notebook"/>.
/// </summary>
public interface INotebookOperations
{
    /// <summary>
    /// Executes a single cell by its identifier.
    /// </summary>
    Task ExecuteCellAsync(Guid cellId);

    /// <summary>
    /// Executes all cells in the notebook in order.
    /// </summary>
    Task ExecuteAllAsync();

    /// <summary>
    /// Executes all cells starting from the specified cell through the end of the notebook.
    /// </summary>
    Task ExecuteFromAsync(Guid cellId);

    /// <summary>
    /// Clears the outputs of a single cell.
    /// </summary>
    Task ClearOutputAsync(Guid cellId);

    /// <summary>
    /// Clears the outputs of all cells in the notebook.
    /// </summary>
    Task ClearAllOutputsAsync();

    /// <summary>
    /// Restarts the specified kernel, or the default kernel if no identifier is provided.
    /// </summary>
    Task RestartKernelAsync(string? kernelId = null);

    /// <summary>
    /// Inserts a new cell at the specified index.
    /// </summary>
    /// <returns>The identifier of the newly created cell.</returns>
    Task<string> InsertCellAsync(int index, string type, string? language = null);

    /// <summary>
    /// Removes a cell by its identifier.
    /// </summary>
    Task RemoveCellAsync(Guid cellId);

    /// <summary>
    /// Moves a cell to a new position in the notebook.
    /// </summary>
    Task MoveCellAsync(Guid cellId, int newIndex);

    /// <summary>
    /// Gets the identifier of the currently active layout.
    /// </summary>
    string? ActiveLayoutId { get; }

    /// <summary>
    /// Switches the active layout engine by its layout identifier.
    /// </summary>
    void SetActiveLayout(string layoutId);

    /// <summary>
    /// Gets the identifier of the currently active theme.
    /// </summary>
    string? ActiveThemeId { get; }

    /// <summary>
    /// Switches the active theme by its theme identifier.
    /// </summary>
    void SetActiveTheme(string themeId);
}
