namespace Verso.Abstractions;

/// <summary>
/// Specifies the visual theme applied to the notebook UI.
/// </summary>
public enum ThemeKind
{
    /// <summary>
    /// A light color scheme with dark text on a light background.
    /// </summary>
    Light,

    /// <summary>
    /// A dark color scheme with light text on a dark background.
    /// </summary>
    Dark,

    /// <summary>
    /// A high-contrast color scheme optimized for accessibility.
    /// </summary>
    HighContrast
}
