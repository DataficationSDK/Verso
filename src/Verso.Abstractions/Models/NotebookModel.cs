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
    /// Gets or sets the qualified <see cref="LayoutReference"/> for the currently active layout,
    /// or <see langword="null"/> if no layout is selected. Legacy bare-string references read from
    /// older notebooks land here with an empty <see cref="LayoutReference.ExtensionId"/> and are
    /// resolved when the notebook is wired up to the extension host.
    /// </summary>
    public LayoutReference? ActiveLayout { get; set; }

    /// <summary>
    /// Convenience getter exposing just the <see cref="LayoutReference.LayoutId"/> half of the
    /// active layout. Retained as a compatibility forwarder for callers that have not yet been
    /// migrated to the qualified <see cref="ActiveLayout"/> shape; scheduled for removal in v2.0.
    /// </summary>
    public string? ActiveLayoutId => ActiveLayout?.LayoutId;

    /// <summary>
    /// True when this model was loaded from a legacy notebook whose <c>activeLayout</c> was
    /// a bare string and required resolution against the loaded extension set. Not persisted;
    /// consumed by the host wire-up to decide whether to run legacy resolution and whether
    /// the next serialize should promote the on-disk form.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool RequiresLegacyLayoutResolution { get; set; }

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
    /// Gets or sets typed parameter definitions keyed by parameter name.
    /// When present, parameters are injected into the variable store before execution.
    /// </summary>
    public Dictionary<string, NotebookParameterDefinition>? Parameters { get; set; }

    /// <summary>
    /// Gets or sets per-extension settings keyed by extension ID. Only overridden values (not defaults) are persisted.
    /// </summary>
    public Dictionary<string, Dictionary<string, object?>> ExtensionSettings { get; set; } = new();
}
