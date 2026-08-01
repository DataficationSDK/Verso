namespace Verso.Blazor.Shared.Models;

/// <summary>
/// Maps the icon names panels declare onto the glyphs this host draws with.
/// </summary>
/// <remarks>
/// A panel names an icon rather than supplying one so that hosts which do not draw
/// SVG can still show something sensible. Resolution order is icon name, then the
/// panel's own markup, then nothing, at which point the caller falls back to the
/// display name. An unrecognized name is not an error.
/// </remarks>
public static class PanelIcons
{
    private const string Attrs =
        "viewBox=\"0 0 16 16\" width=\"15\" height=\"15\" fill=\"none\" stroke=\"currentColor\" " +
        "stroke-width=\"1.3\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";

    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["document"] = $"<svg {Attrs}><rect x=\"3\" y=\"1.5\" width=\"10\" height=\"13\" rx=\"1\"/>"
            + "<line x1=\"5.5\" y1=\"5\" x2=\"10.5\" y2=\"5\"/><line x1=\"5.5\" y1=\"7.5\" x2=\"10.5\" y2=\"7.5\"/>"
            + "<line x1=\"5.5\" y1=\"10\" x2=\"8.5\" y2=\"10\"/></svg>",

        ["puzzle"] = $"<svg {Attrs}><path d=\"M6.5 2.5h3v3h3v3h-3v3h-3v-3h-3v-3h3z\"/></svg>",

        ["braces"] = $"<svg {Attrs}><path d=\"M4.5 2.5c-1.5 0-2 1-2 2s1 1.5 1 2.5-.5 1-1 1.5c.5.5 1 .5 1 1.5s-1 1.5-1 2.5.5 2 2 2\"/>"
            + "<path d=\"M11.5 2.5c1.5 0 2 1 2 2s-1 1.5-1 2.5.5 1 1 1.5c-.5.5-1 .5-1 1.5s1 1.5 1 2.5-.5 2-2 2\"/>"
            + "<line x1=\"6.5\" y1=\"6\" x2=\"9.5\" y2=\"10\"/><line x1=\"6.5\" y1=\"10\" x2=\"9.5\" y2=\"6\"/></svg>",

        ["gear"] = $"<svg {Attrs}><circle cx=\"8\" cy=\"8\" r=\"2.5\"/>"
            + "<path d=\"M8 1.5v2M8 12.5v2M1.5 8h2M12.5 8h2M3.1 3.1l1.4 1.4M11.5 11.5l1.4 1.4M3.1 12.9l1.4-1.4M11.5 4.5l1.4-1.4\"/></svg>",

        ["list"] = $"<svg {Attrs}><rect x=\"2\" y=\"2\" width=\"12\" height=\"12\" rx=\"1.5\"/>"
            + "<line x1=\"5\" y1=\"5.5\" x2=\"11\" y2=\"5.5\"/><line x1=\"5\" y1=\"8\" x2=\"9\" y2=\"8\"/>"
            + "<line x1=\"5\" y1=\"10.5\" x2=\"10\" y2=\"10.5\"/></svg>",

        ["search"] = $"<svg {Attrs}><circle cx=\"7\" cy=\"7\" r=\"4.5\"/><line x1=\"10.4\" y1=\"10.4\" x2=\"14\" y2=\"14\"/></svg>",

        ["layout"] = $"<svg {Attrs}><rect x=\"1.5\" y=\"1.5\" width=\"13\" height=\"13\" rx=\"1\"/>"
            + "<line x1=\"6\" y1=\"1.5\" x2=\"6\" y2=\"14.5\"/><line x1=\"6\" y1=\"8\" x2=\"14.5\" y2=\"8\"/></svg>",

        ["compare"] = $"<svg {Attrs}><rect x=\"1.5\" y=\"1.5\" width=\"5.5\" height=\"13\" rx=\"1\"/>"
            + "<rect x=\"9\" y=\"1.5\" width=\"5.5\" height=\"13\" rx=\"1\"/></svg>",

        ["info"] = $"<svg {Attrs}><circle cx=\"8\" cy=\"8\" r=\"6.2\"/><line x1=\"8\" y1=\"7.2\" x2=\"8\" y2=\"11.2\"/>"
            + "<line x1=\"8\" y1=\"4.9\" x2=\"8\" y2=\"5\"/></svg>",

        ["warning"] = $"<svg {Attrs}><path d=\"M8 2.2 14.4 13.4H1.6z\"/><line x1=\"8\" y1=\"6.4\" x2=\"8\" y2=\"9.5\"/>"
            + "<line x1=\"8\" y1=\"11.4\" x2=\"8\" y2=\"11.5\"/></svg>",

        ["flag"] = $"<svg {Attrs}><line x1=\"3.5\" y1=\"1.8\" x2=\"3.5\" y2=\"14.2\"/>"
            + "<path d=\"M3.5 2.8h8l-1.8 2.6L11.5 8h-8z\"/></svg>",

        ["check"] = $"<svg {Attrs}><path d=\"M2.8 8.4 6.2 11.8 13.2 4.6\"/></svg>",

        ["clock"] = $"<svg {Attrs}><circle cx=\"8\" cy=\"8\" r=\"6.2\"/><path d=\"M8 4.4V8l2.6 1.6\"/></svg>",

        ["tag"] = $"<svg {Attrs}><path d=\"M2.4 7.6V2.4h5.2l6 6-5.2 5.2z\"/><line x1=\"5\" y1=\"5\" x2=\"5.1\" y2=\"5\"/></svg>",

        ["chart"] = $"<svg {Attrs}><line x1=\"2.2\" y1=\"13.4\" x2=\"13.8\" y2=\"13.4\"/>"
            + "<rect x=\"3.4\" y=\"8\" width=\"2.4\" height=\"5.4\"/><rect x=\"6.8\" y=\"4.6\" width=\"2.4\" height=\"8.8\"/>"
            + "<rect x=\"10.2\" y=\"6.6\" width=\"2.4\" height=\"6.8\"/></svg>",

        ["table"] = $"<svg {Attrs}><rect x=\"2\" y=\"2.6\" width=\"12\" height=\"10.8\" rx=\"1\"/>"
            + "<line x1=\"2\" y1=\"6.2\" x2=\"14\" y2=\"6.2\"/><line x1=\"6.4\" y1=\"6.2\" x2=\"6.4\" y2=\"13.4\"/></svg>",

        ["folder"] = $"<svg {Attrs}><path d=\"M1.8 4.2h4.4l1.4 1.8h6.6v7.2H1.8z\"/></svg>",

        ["link"] = $"<svg {Attrs}><path d=\"M6.6 9.4a2.8 2.8 0 0 0 4 0l2-2a2.8 2.8 0 1 0-4-4l-1 1\"/>"
            + "<path d=\"M9.4 6.6a2.8 2.8 0 0 0-4 0l-2 2a2.8 2.8 0 1 0 4 4l1-1\"/></svg>",
    };

    /// <summary>
    /// Resolves icon markup for a panel, preferring the named glyph and falling back
    /// to markup the panel supplied itself. Returns <c>null</c> when neither yields
    /// anything, which tells the caller to show the display name instead.
    /// </summary>
    public static string? Resolve(string? iconName, string? iconMarkup)
    {
        if (!string.IsNullOrWhiteSpace(iconName) && Glyphs.TryGetValue(iconName, out var glyph))
            return glyph;

        return string.IsNullOrWhiteSpace(iconMarkup) ? null : iconMarkup;
    }

    /// <summary>
    /// The initial shown when a panel resolves to no icon at all.
    /// </summary>
    public static string InitialFor(string displayName)
        => string.IsNullOrWhiteSpace(displayName) ? "?" : displayName.Trim()[..1].ToUpperInvariant();

    /// <summary>
    /// The icon names this host draws. Exposed so documentation and tests stay in
    /// step with what actually ships.
    /// </summary>
    public static IReadOnlyCollection<string> KnownNames => Glyphs.Keys;
}
