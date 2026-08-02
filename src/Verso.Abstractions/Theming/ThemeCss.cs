using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Verso.Abstractions;

/// <summary>
/// Builds the <c>:root { --verso-*: ...; }</c> custom property block that Verso surfaces
/// style themselves from.
/// </summary>
/// <remarks>
/// <para>
/// There is one emitter rather than one per surface. The notebook interface and the
/// self-contained HTML export need the same block, and while each kept its own copy the two
/// drifted: the export never emitted the typography tokens that are not fonts, so
/// <c>--verso-font-family-mono</c>, <c>--verso-font-family-sans</c> and
/// <c>--verso-font-size-base</c> were missing from every exported document.
/// </para>
/// <para>
/// Every number is written with the invariant culture. A stylesheet is not read in anyone's
/// language: a length is <c>1.4</c> whoever is looking at it, never <c>1,4</c>, and a browser
/// discards a declaration it cannot parse. Callers cannot get this wrong by accident because
/// they never format a number themselves.
/// </para>
/// </remarks>
public static class ThemeCss
{
    /// <summary>
    /// Builds the custom property block for a theme, falling back to the default tokens for
    /// anything the theme does not supply.
    /// </summary>
    public static string BuildRootBlock(ITheme? theme) =>
        BuildRootBlock(theme?.Colors, theme?.Typography, theme?.Spacing, theme?.Elevation);

    /// <summary>
    /// Builds the custom property block from individual token groups, falling back to the
    /// defaults for any group that is <c>null</c>.
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ThemeColorTokens))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ThemeTypography))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ThemeSpacing))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ThemeElevation))]
    public static string BuildRootBlock(
        ThemeColorTokens? colors,
        ThemeTypography? typography,
        ThemeSpacing? spacing,
        ThemeElevation? elevation)
    {
        var sb = new StringBuilder();
        sb.AppendLine(":root {");
        AppendColors(sb, colors ?? new ThemeColorTokens());
        AppendTypography(sb, typography ?? new ThemeTypography());
        AppendSpacing(sb, spacing ?? new ThemeSpacing());
        AppendElevation(sb, elevation ?? new ThemeElevation());
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendColors(StringBuilder sb, ThemeColorTokens colors)
    {
        foreach (var prop in typeof(ThemeColorTokens).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(string)) continue;
            var value = (string?)prop.GetValue(colors) ?? "";
            sb.AppendLine($"  --verso-{ToKebabCase(prop.Name)}: {value};");
        }
    }

    private static void AppendTypography(StringBuilder sb, ThemeTypography typography)
    {
        foreach (var prop in typeof(ThemeTypography).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = ToKebabCase(prop.Name);
            if (prop.PropertyType == typeof(FontDescriptor))
            {
                if (prop.GetValue(typography) is not FontDescriptor font) continue;
                sb.AppendLine($"  --verso-{name}-family: {font.Family};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  --verso-{name}-size: {font.SizePx}px;");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  --verso-{name}-weight: {font.Weight};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  --verso-{name}-line-height: {font.LineHeight};");
            }
            else if (prop.PropertyType == typeof(string))
            {
                if (prop.GetValue(typography) is not string value) continue;
                sb.AppendLine($"  --verso-{name}: {value};");
            }
            else if (prop.PropertyType == typeof(double))
            {
                var value = (double)prop.GetValue(typography)!;
                sb.AppendLine(CultureInfo.InvariantCulture, $"  --verso-{name}: {value}px;");
            }
        }
    }

    private static void AppendSpacing(StringBuilder sb, ThemeSpacing spacing)
    {
        foreach (var prop in typeof(ThemeSpacing).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(double)) continue;
            var value = (double)prop.GetValue(spacing)!;
            sb.AppendLine(CultureInfo.InvariantCulture, $"  --verso-{ToKebabCase(prop.Name)}: {value}px;");
        }
    }

    private static void AppendElevation(StringBuilder sb, ThemeElevation elevation)
    {
        // Elevation properties are named Level0..Level3. The prefix is dropped so a stylesheet
        // reads var(--verso-elevation-1) rather than var(--verso-elevation-level1).
        foreach (var prop in typeof(ThemeElevation).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(string)) continue;
            if (prop.GetValue(elevation) is not string value) continue;
            var suffix = ToKebabCase(prop.Name.StartsWith("Level", StringComparison.Ordinal)
                ? prop.Name["Level".Length..]
                : prop.Name);
            sb.AppendLine($"  --verso-elevation-{suffix}: {value};");
        }
    }

    /// <summary>
    /// Converts a PascalCase property name to the spelling used in a custom property name,
    /// so <c>BgDefault</c> becomes <c>bg-default</c>.
    /// </summary>
    private static string ToKebabCase(string name)
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
