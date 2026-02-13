namespace Verso.Abstractions;

/// <summary>
/// Renders the input editor area and output display area of a cell.
/// Each renderer is associated with a specific cell type.
/// </summary>
public interface ICellRenderer : IExtension
{
    /// <summary>
    /// Identifier of the cell type this renderer handles, matching <see cref="ICellType.CellTypeId"/>.
    /// </summary>
    string CellTypeId { get; }

    /// <summary>
    /// Human-readable name for this renderer, shown in UI menus and settings.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Renders the input (editor) region of a cell from its source text.
    /// </summary>
    /// <param name="source">The raw source content of the cell.</param>
    /// <param name="context">Rendering context providing theme, dimensions, and helper APIs.</param>
    /// <returns>A render result containing the visual representation of the cell input.</returns>
    Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context);

    /// <summary>
    /// Renders a single output produced by cell execution.
    /// </summary>
    /// <param name="output">The cell output to render.</param>
    /// <param name="context">Rendering context providing theme, dimensions, and helper APIs.</param>
    /// <returns>A render result containing the visual representation of the output.</returns>
    Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context);

    /// <summary>
    /// Returns the editor language identifier (e.g. "markdown", "csharp") for syntax highlighting,
    /// or <c>null</c> if the cell does not use a text editor.
    /// </summary>
    /// <returns>An editor language identifier, or <c>null</c>.</returns>
    string? GetEditorLanguage();
}
