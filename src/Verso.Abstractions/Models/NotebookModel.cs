namespace Verso.Abstractions;

/// <summary>
/// Mutable model representing a full Verso notebook document, including its cells, metadata, and layout configuration.
/// </summary>
public sealed class NotebookModel
{
    /// <summary>
    /// Gets or sets the notebook file format version string.
    /// </summary>
    public string FormatVersion { get; set; } = NotebookFormatVersion.Current;

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
    /// Convenience accessor exposing just the <see cref="LayoutReference.LayoutId"/> half of the
    /// active layout. Retained as a compatibility forwarder for callers that have not yet been
    /// migrated to the qualified <see cref="ActiveLayout"/> shape, and slated for removal in a
    /// future release.
    /// </summary>
    /// <remarks>
    /// The setter is obsolete. A bare layout id cannot name its owning extension, so an assignment
    /// produces an unqualified <see cref="ActiveLayout"/> that is resolved against the loaded
    /// extension set at wire-up time, and only when exactly one loaded layout carries that id. When
    /// the assigned id matches the current <see cref="ActiveLayout"/>, the existing
    /// <see cref="LayoutReference.ExtensionId"/> is preserved so a qualified reference is not
    /// silently downgraded to an unqualified one. Set <see cref="ActiveLayout"/> directly to name an
    /// unambiguous layout.
    /// </remarks>
    public string? ActiveLayoutId
    {
        get => ActiveLayout?.LayoutId;
        [Obsolete("Set ActiveLayout (a qualified LayoutReference) instead. A bare layout id resolves only when exactly one loaded layout carries it, and this setter is slated for removal in a future release.")]
        set
        {
            if (value is null)
            {
                ActiveLayout = null;
                return;
            }

            // Preserve the owning extension id when the id is unchanged so a qualified
            // reference is not silently downgraded; otherwise fall back to an unqualified
            // reference that is resolved against the loaded extension set at wire-up.
            var extensionId = ActiveLayout is { } current
                && string.Equals(current.LayoutId, value, StringComparison.OrdinalIgnoreCase)
                ? current.ExtensionId
                : string.Empty;

            ActiveLayout = new LayoutReference(extensionId, value);
        }
    }

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
