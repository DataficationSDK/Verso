namespace Verso.Abstractions;

/// <summary>
/// Identity pair for a layout, composed of the owning extension id and the layout id
/// declared on the extension's <see cref="ILayoutEngine"/>. The pair is the only valid
/// identity for a layout from v1.0 onward: <see cref="LayoutId"/> alone is not unique
/// because two extensions may each register a layout with the same id.
/// </summary>
/// <param name="ExtensionId">
/// The <see cref="IExtension.ExtensionId"/> of the extension that owns the layout.
/// An empty string is reserved as a sentinel for unqualified legacy references that
/// have not yet been resolved against the loaded extension set.
/// </param>
/// <param name="LayoutId">The layout's <see cref="ILayoutEngine.LayoutId"/>.</param>
public readonly record struct LayoutReference(string ExtensionId, string LayoutId)
{
    /// <summary>
    /// The qualified string form <c>"&lt;extensionId&gt;:&lt;layoutId&gt;"</c> used as the key
    /// in the notebook's persisted <c>layouts</c> map.
    /// </summary>
    public string Qualified => $"{ExtensionId}:{LayoutId}";

    /// <summary>
    /// True when this reference is missing its <see cref="ExtensionId"/>. This indicates
    /// a legacy bare-string <c>activeLayout</c> that has not yet been resolved against
    /// the loaded extension set.
    /// </summary>
    public bool IsUnqualified => string.IsNullOrEmpty(ExtensionId);

    /// <summary>
    /// Parses a qualified-string of the form <c>"&lt;extensionId&gt;:&lt;layoutId&gt;"</c>.
    /// Returns <c>false</c> when the input has no colon or has empty parts. A bare layout id
    /// is treated as unqualified and returned with an empty <see cref="ExtensionId"/>.
    /// </summary>
    public static bool TryParse(string? qualified, out LayoutReference reference)
    {
        reference = default;
        if (string.IsNullOrWhiteSpace(qualified)) return false;

        var idx = qualified.IndexOf(':');
        if (idx <= 0)
        {
            // Bare id with no qualifier — treat as unqualified.
            reference = new LayoutReference(string.Empty, qualified);
            return true;
        }

        var ext = qualified.Substring(0, idx);
        var id = qualified.Substring(idx + 1);
        if (string.IsNullOrEmpty(id)) return false;

        reference = new LayoutReference(ext, id);
        return true;
    }

    public override string ToString() => Qualified;
}
