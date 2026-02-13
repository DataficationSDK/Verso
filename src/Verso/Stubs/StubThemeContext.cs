using System.Reflection;
using Verso.Abstractions;

namespace Verso.Stubs;

/// <summary>
/// Stub <see cref="IThemeContext"/> that returns default light-theme values from
/// <see cref="ThemeColorTokens"/>, <see cref="ThemeTypography"/>, and <see cref="ThemeSpacing"/>.
/// </summary>
public sealed class StubThemeContext : IThemeContext
{
    private static readonly Lazy<Dictionary<string, string>> ColorMap = new(BuildColorMap);
    private static readonly Lazy<Dictionary<string, FontDescriptor>> FontMap = new(BuildFontMap);
    private static readonly Lazy<Dictionary<string, double>> SpacingMap = new(BuildSpacingMap);

    /// <inheritdoc />
    public ThemeKind ThemeKind => ThemeKind.Light;

    /// <inheritdoc />
    public string GetColor(string tokenName)
    {
        ArgumentNullException.ThrowIfNull(tokenName);
        return ColorMap.Value.TryGetValue(tokenName, out var color) ? color : "";
    }

    /// <inheritdoc />
    public FontDescriptor GetFont(string fontRole)
    {
        ArgumentNullException.ThrowIfNull(fontRole);
        return FontMap.Value.TryGetValue(fontRole, out var font) ? font : new FontDescriptor("sans-serif", 13);
    }

    /// <inheritdoc />
    public double GetSpacing(string spacingName)
    {
        ArgumentNullException.ThrowIfNull(spacingName);
        return SpacingMap.Value.TryGetValue(spacingName, out var value) ? value : 0;
    }

    /// <inheritdoc />
    public string? GetSyntaxColor(string tokenType) => null;

    /// <inheritdoc />
    public string? GetCustomToken(string key) => null;

    private static Dictionary<string, string> BuildColorMap()
    {
        var defaults = new ThemeColorTokens();
        return typeof(ThemeColorTokens)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .ToDictionary(p => p.Name, p => (string)p.GetValue(defaults)!, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, FontDescriptor> BuildFontMap()
    {
        var defaults = new ThemeTypography();
        return typeof(ThemeTypography)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(FontDescriptor))
            .ToDictionary(p => p.Name, p => (FontDescriptor)p.GetValue(defaults)!, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, double> BuildSpacingMap()
    {
        var defaults = new ThemeSpacing();
        return typeof(ThemeSpacing)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(double))
            .ToDictionary(p => p.Name, p => (double)p.GetValue(defaults)!, StringComparer.OrdinalIgnoreCase);
    }
}
