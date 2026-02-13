namespace Verso.Abstractions;

/// <summary>
/// Defines a cell type by combining a renderer with an optional language kernel.
/// Cell types appear in the cell type picker and determine how a cell is edited,
/// displayed, and optionally executed.
/// </summary>
public interface ICellType : IExtension
{
    /// <summary>
    /// Unique identifier for this cell type (e.g. "code-csharp", "markdown").
    /// </summary>
    string CellTypeId { get; }

    /// <summary>
    /// Human-readable label shown in the cell type picker.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Optional icon name or path displayed alongside the cell type in menus.
    /// </summary>
    string? Icon { get; }

    /// <summary>
    /// Renderer responsible for producing the visual representation of cells of this type.
    /// </summary>
    ICellRenderer Renderer { get; }

    /// <summary>
    /// Optional language kernel used to execute cells of this type.
    /// Returns <c>null</c> for non-executable cell types such as Markdown or raw text.
    /// </summary>
    ILanguageKernel? Kernel { get; }

    /// <summary>
    /// Indicates whether the user can edit the cell content. Non-editable cells are read-only.
    /// </summary>
    bool IsEditable { get; }

    /// <summary>
    /// Returns the default source content inserted when a new cell of this type is created.
    /// </summary>
    /// <returns>A string containing the default cell content.</returns>
    string GetDefaultContent();
}
