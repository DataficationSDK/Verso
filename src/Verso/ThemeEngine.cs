using System.Reflection;
using Verso.Abstractions;

namespace Verso;

/// <summary>
/// Manages the active theme and implements <see cref="IThemeContext"/> by delegating
/// to the currently active <see cref="ITheme"/> extension.
/// </summary>
public sealed class ThemeEngine : IThemeContext
{
    private readonly IReadOnlyList<ITheme> _availableThemes;
    private volatile ITheme? _activeTheme;

    // Fallback maps (same as StubThemeContext) when no theme is active
    private static readonly Lazy<Dictionary<string, string>> FallbackColorMap = new(BuildColorMap);
    private static readonly Lazy<Dictionary<string, FontDescriptor>> FallbackFontMap = new(BuildFontMap);
    private static readonly Lazy<Dictionary<string, double>> FallbackSpacingMap = new(BuildSpacingMap);

    public ThemeEngine(IReadOnlyList<ITheme> availableThemes, string? defaultThemeId = null)
    {
        _availableThemes = availableThemes ?? throw new ArgumentNullException(nameof(availableThemes));

        if (defaultThemeId is not null)
            SetActiveTheme(defaultThemeId);
    }

    /// <summary>
    /// Gets the currently active theme, or <c>null</c> if no theme is active.
    /// </summary>
    public ITheme? ActiveTheme => _activeTheme;

    /// <summary>
    /// Gets the list of available themes.
    /// </summary>
    public IReadOnlyList<ITheme> AvailableThemes => _availableThemes;

    /// <summary>
    /// Raised when the active theme changes.
    /// </summary>
    public event Action<ITheme>? OnThemeChanged;

    /// <summary>
    /// Switches the active theme by theme ID.
    /// </summary>
    public void SetActiveTheme(string themeId)
    {
        ArgumentNullException.ThrowIfNull(themeId);
        var theme = _availableThemes.FirstOrDefault(
            t => string.Equals(t.ThemeId, themeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Theme '{themeId}' not found.");
        _activeTheme = theme;
        OnThemeChanged?.Invoke(theme);
    }

    // --- IThemeContext ---

    public ThemeKind ThemeKind => _activeTheme?.ThemeKind ?? ThemeKind.Light;

    public string GetColor(string tokenName)
    {
        ArgumentNullException.ThrowIfNull(tokenName);

        var theme = _activeTheme;
        if (theme is not null)
        {
            var colors = theme.Colors;
            var prop = typeof(ThemeColorTokens).GetProperty(tokenName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is not null && prop.PropertyType == typeof(string))
                return (string)prop.GetValue(colors)!;
            return "";
        }

        return FallbackColorMap.Value.TryGetValue(tokenName, out var color) ? color : "";
    }

    public FontDescriptor GetFont(string fontRole)
    {
        ArgumentNullException.ThrowIfNull(fontRole);

        var theme = _activeTheme;
        if (theme is not null)
        {
            var typography = theme.Typography;
            var prop = typeof(ThemeTypography).GetProperty(fontRole,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is not null && prop.PropertyType == typeof(FontDescriptor))
                return (FontDescriptor)prop.GetValue(typography)!;
            return new FontDescriptor("sans-serif", 13);
        }

        return FallbackFontMap.Value.TryGetValue(fontRole, out var font) ? font : new FontDescriptor("sans-serif", 13);
    }

    public double GetSpacing(string spacingName)
    {
        ArgumentNullException.ThrowIfNull(spacingName);

        var theme = _activeTheme;
        if (theme is not null)
        {
            var spacing = theme.Spacing;
            var prop = typeof(ThemeSpacing).GetProperty(spacingName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is not null && prop.PropertyType == typeof(double))
                return (double)prop.GetValue(spacing)!;
            return 0;
        }

        return FallbackSpacingMap.Value.TryGetValue(spacingName, out var value) ? value : 0;
    }

    public string? GetSyntaxColor(string tokenType)
    {
        ArgumentNullException.ThrowIfNull(tokenType);
        var theme = _activeTheme;
        return theme?.GetSyntaxColors().Get(tokenType);
    }

    public string? GetCustomToken(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _activeTheme?.GetCustomToken(key);
    }

    // --- Fallback map builders (same as StubThemeContext) ---

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
