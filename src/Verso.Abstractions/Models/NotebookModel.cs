namespace Verso.Abstractions;

/// <summary>
/// Mutable model representing a full Verso notebook document, including its cells, metadata, and layout configuration.
/// </summary>
public sealed class NotebookModel
{
    /// <summary>
    /// Gets or sets the notebook file format version string.
    /// </summary>
    public string FormatVersion { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets the display title of the notebook, or <see langword="null"/> if untitled.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the notebook was created.
    /// </summary>
    public DateTimeOffset? Created { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the notebook was last modified.
    /// </summary>
    public DateTimeOffset? Modified { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the default language kernel used for new code cells.
    /// </summary>
    public string? DefaultKernelId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the currently active layout, or <see langword="null"/> if no layout is selected.
    /// </summary>
    public string? ActiveLayoutId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the preferred theme, or <see langword="null"/> if no theme preference is set.
    /// </summary>
    public string? PreferredThemeId { get; set; }

    /// <summary>
    /// Gets or sets the list of extension identifiers that must be loaded for this notebook to function.
    /// </summary>
    public List<string> RequiredExtensions { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of extension identifiers that may enhance this notebook but are not required.
    /// </summary>
    public List<string> OptionalExtensions { get; set; } = new();

    /// <summary>
    /// Gets or sets the ordered list of cells that make up the notebook content.
    /// </summary>
    public List<CellModel> Cells { get; set; } = new();

    /// <summary>
    /// Gets or sets named layout definitions that control how cells are arranged in the notebook UI.
    /// </summary>
    public Dictionary<string, object> Layouts { get; set; } = new();

    /// <summary>
    /// Gets or sets per-extension settings keyed by extension ID. Only overridden values (not defaults) are persisted.
    /// </summary>
    public Dictionary<string, Dictionary<string, object?>> ExtensionSettings { get; set; } = new();
}
