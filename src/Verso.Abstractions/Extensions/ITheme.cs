namespace Verso.Abstractions;

/// <summary>
/// Defines a complete visual theme for the Verso notebook UI, including colors,
/// typography, spacing, and syntax highlighting.
/// </summary>
public interface ITheme : IExtension
{
    /// <summary>
    /// Unique identifier for the theme (e.g. "verso-dark", "solarized-light").
    /// </summary>
    string ThemeId { get; }

    /// <summary>
    /// Human-readable theme name shown in the theme picker.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Indicates whether this is a light, dark, or high-contrast theme.
    /// </summary>
    ThemeKind ThemeKind { get; }

    /// <summary>
    /// Color tokens that define backgrounds, foregrounds, borders, and accent colors.
    /// </summary>
    ThemeColorTokens Colors { get; }

    /// <summary>
    /// Typography settings including font families, sizes, and line heights.
    /// </summary>
    ThemeTypography Typography { get; }

    /// <summary>
    /// Spacing tokens controlling padding, margins, and gaps throughout the UI.
    /// </summary>
    ThemeSpacing Spacing { get; }

    /// <summary>
    /// Returns the value of a custom theme token, allowing extensions to define
    /// and consume additional design tokens beyond the built-in set.
    /// </summary>
    /// <param name="key">The custom token key.</param>
    /// <returns>The token value, or <c>null</c> if the key is not defined by this theme.</returns>
    string? GetCustomToken(string key);

    /// <summary>
    /// Returns the syntax highlighting color map used by code editors within this theme.
    /// </summary>
    /// <returns>A mapping of syntax token types to their colors.</returns>
    SyntaxColorMap GetSyntaxColors();
}
