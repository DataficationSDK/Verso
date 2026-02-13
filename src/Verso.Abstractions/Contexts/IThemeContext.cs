namespace Verso.Abstractions;

/// <summary>
/// Provides read-only access to the active visual theme, including colors, fonts, and spacing.
/// </summary>
public interface IThemeContext
{
    /// <summary>
    /// Gets the current theme kind (e.g., light or dark).
    /// </summary>
    ThemeKind ThemeKind { get; }

    /// <summary>
    /// Resolves a named color token to its current theme value.
    /// </summary>
    /// <param name="tokenName">The color token name to resolve.</param>
    /// <returns>The color value as a string (e.g., a hex color code).</returns>
    string GetColor(string tokenName);

    /// <summary>
    /// Retrieves the font descriptor for the specified font role.
    /// </summary>
    /// <param name="fontRole">The font role identifier (e.g., "body", "code").</param>
    /// <returns>A <see cref="FontDescriptor"/> containing the font family, size, and weight.</returns>
    FontDescriptor GetFont(string fontRole);

    /// <summary>
    /// Retrieves the spacing value for the specified spacing token.
    /// </summary>
    /// <param name="spacingName">The spacing token name to resolve.</param>
    /// <returns>The spacing value in device-independent units.</returns>
    double GetSpacing(string spacingName);

    /// <summary>
    /// Resolves a syntax-highlighting token type to its color value.
    /// </summary>
    /// <param name="tokenType">The syntax token type (e.g., "keyword", "string").</param>
    /// <returns>The color value as a string, or <c>null</c> if the token type is not defined.</returns>
    string? GetSyntaxColor(string tokenType);

    /// <summary>
    /// Retrieves a custom theme token value by key.
    /// </summary>
    /// <param name="key">The custom token key.</param>
    /// <returns>The token value as a string, or <c>null</c> if the key is not defined.</returns>
    string? GetCustomToken(string key);
}
