namespace Verso.Abstractions;

/// <summary>
/// Defines the elevation (drop shadow) scale for a Verso notebook theme.
/// Each value is a complete CSS <c>box-shadow</c> expression, or <c>none</c>.
/// </summary>
/// <remarks>
/// Elevation is how one surface separates from the one beneath it. Themes that
/// separate surfaces by lightness alone (most dark themes) can lean on small
/// shadows; themes whose surfaces compress near white (most light themes) need
/// the shadow to carry more of the work, and high-contrast themes should set
/// every level to <c>none</c> and separate with borders instead.
///
/// The emitted custom properties drop the <c>Level</c> prefix, so a stylesheet
/// reads <c>var(--verso-elevation-1)</c> rather than <c>--verso-elevation-level1</c>.
/// </remarks>
public sealed record ThemeElevation
{
    /// <summary>No elevation. A surface flush with its parent.</summary>
    public string Level0 { get; init; } = "none";

    /// <summary>Resting cards: notebook cells, panels, and other content surfaces.</summary>
    public string Level1 { get; init; } = "0 1px 2px rgba(16, 18, 32, 0.06), 0 1px 3px rgba(16, 18, 32, 0.10)";

    /// <summary>Raised cards: hover and active states of anything at level 1.</summary>
    public string Level2 { get; init; } = "0 2px 6px rgba(16, 18, 32, 0.08), 0 4px 12px rgba(16, 18, 32, 0.10)";

    /// <summary>Floating surfaces that overlay the page: dialogs, dropdowns, popovers.</summary>
    public string Level3 { get; init; } = "0 12px 32px rgba(16, 18, 32, 0.14), 0 2px 8px rgba(16, 18, 32, 0.10)";
}
