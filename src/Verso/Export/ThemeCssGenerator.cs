using System.Reflection;
using System.Text;
using Verso.Abstractions;

namespace Verso.Export;

/// <summary>
/// Generates CSS custom property blocks from Verso theme tokens.
/// Extracted from the Blazor ThemeProvider logic for use in self-contained HTML export.
/// </summary>
internal static class ThemeCssGenerator
{
    /// <summary>
    /// Builds a <c>:root { --verso-xxx: value; }</c> CSS block from the given theme,
    /// or from default tokens when <paramref name="theme"/> is <c>null</c>.
    /// </summary>
    public static string BuildCss(ITheme? theme)
    {
        var sb = new StringBuilder();
        sb.AppendLine(":root {");

        // Color tokens
        var colors = theme?.Colors ?? new ThemeColorTokens();
        foreach (var prop in typeof(ThemeColorTokens).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(string)) continue;
            var value = (string?)prop.GetValue(colors) ?? "";
            var cssName = ToKebabCase(prop.Name);
            sb.AppendLine($"  --verso-{cssName}: {value};");
        }

        // Typography tokens
        var typography = theme?.Typography ?? new ThemeTypography();
        foreach (var prop in typeof(ThemeTypography).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(FontDescriptor)) continue;
            var font = (FontDescriptor?)prop.GetValue(typography);
            if (font is null) continue;
            var cssName = ToKebabCase(prop.Name);
            sb.AppendLine($"  --verso-{cssName}-family: {font.Family};");
            sb.AppendLine($"  --verso-{cssName}-size: {font.SizePx}px;");
            sb.AppendLine($"  --verso-{cssName}-weight: {font.Weight};");
            sb.AppendLine($"  --verso-{cssName}-line-height: {font.LineHeight};");
        }

        // Spacing tokens
        var spacing = theme?.Spacing ?? new ThemeSpacing();
        foreach (var prop in typeof(ThemeSpacing).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(double)) continue;
            var value = (double)prop.GetValue(spacing)!;
            var cssName = ToKebabCase(prop.Name);
            sb.AppendLine($"  --verso-{cssName}: {value}px;");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    internal static string ToKebabCase(string name)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
