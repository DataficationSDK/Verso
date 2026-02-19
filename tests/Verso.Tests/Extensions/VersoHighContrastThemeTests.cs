using System.Reflection;
using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Extensions.Themes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class VersoHighContrastThemeTests
{
    private readonly VersoHighContrastTheme _hc = new();
    private readonly VersoDarkTheme _dark = new();

    // --- Metadata ---

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.theme.highcontrast", _hc.ExtensionId);

    [TestMethod]
    public void ThemeKind_IsHighContrast()
        => Assert.AreEqual(ThemeKind.HighContrast, _hc.ThemeKind);

    [TestMethod]
    public void ThemeId_IsCorrect()
        => Assert.AreEqual("verso-highcontrast", _hc.ThemeId);

    [TestMethod]
    public void DisplayName_IsCorrect()
        => Assert.AreEqual("Verso High Contrast", _hc.DisplayName);

    // --- Color Token Validity ---

    [TestMethod]
    public void AllColorTokens_AreValidHex()
    {
        var hexRegex = new Regex(@"^#[0-9A-Fa-f]{6}$");
        var props = typeof(ThemeColorTokens)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string));

        foreach (var prop in props)
        {
            var value = (string)prop.GetValue(_hc.Colors)!;
            Assert.IsTrue(hexRegex.IsMatch(value),
                $"Color token {prop.Name} has invalid hex value: {value}");
        }
    }

    [TestMethod]
    public void Colors_DifferFromDark()
    {
        Assert.AreNotEqual(_dark.Colors.EditorBackground, _hc.Colors.EditorBackground);
        Assert.AreNotEqual(_dark.Colors.AccentPrimary, _hc.Colors.AccentPrimary);
        Assert.AreNotEqual(_dark.Colors.CellBorder, _hc.Colors.CellBorder);
    }

    // --- Syntax Colors ---

    [TestMethod]
    public void SyntaxColors_HasAtLeast12Tokens()
    {
        var map = _hc.GetSyntaxColors();
        Assert.IsTrue(map.Count >= 12, $"Expected >=12 syntax colors, got {map.Count}");
    }

    [TestMethod]
    public void SyntaxColors_AreValidHex()
    {
        var hexRegex = new Regex(@"^#[0-9A-Fa-f]{6}$");
        var map = _hc.GetSyntaxColors();

        foreach (var kvp in map.GetAll())
        {
            Assert.IsTrue(hexRegex.IsMatch(kvp.Value),
                $"Syntax color {kvp.Key} has invalid hex value: {kvp.Value}");
        }
    }

    // --- Typography ---

    [TestMethod]
    public void Typography_HasLargerFontSizes()
    {
        var defaultTypo = new ThemeTypography();
        Assert.IsTrue(_hc.Typography.EditorFont.SizePx >= defaultTypo.EditorFont.SizePx,
            "High contrast should use equal or larger editor font");
        Assert.IsTrue(_hc.Typography.UIFont.SizePx >= defaultTypo.UIFont.SizePx,
            "High contrast should use equal or larger UI font");
    }

    // --- Spacing ---

    [TestMethod]
    public void Spacing_HasZeroBorderRadius()
    {
        Assert.AreEqual(0, _hc.Spacing.CellBorderRadius);
        Assert.AreEqual(0, _hc.Spacing.ButtonBorderRadius);
    }

    // --- WCAG 2.1 AA Contrast Verification ---

    [TestMethod]
    public void EditorForeground_MeetsAA_AgainstEditorBackground()
        => AssertContrastRatio(_hc.Colors.EditorForeground, _hc.Colors.EditorBackground, 4.5, "EditorForeground/EditorBackground");

    [TestMethod]
    public void AccentPrimary_MeetsAA_AgainstEditorBackground()
        => AssertContrastRatio(_hc.Colors.AccentPrimary, _hc.Colors.EditorBackground, 4.5, "AccentPrimary/EditorBackground");

    [TestMethod]
    public void AccentSecondary_MeetsAA_AgainstEditorBackground()
        => AssertContrastRatio(_hc.Colors.AccentSecondary, _hc.Colors.EditorBackground, 4.5, "AccentSecondary/EditorBackground");

    [TestMethod]
    public void StatusError_MeetsAA_AgainstEditorBackground()
        => AssertContrastRatio(_hc.Colors.StatusError, _hc.Colors.EditorBackground, 4.5, "StatusError/EditorBackground");

    [TestMethod]
    public void CellErrorForeground_MeetsAA_AgainstCellErrorBackground()
        => AssertContrastRatio(_hc.Colors.CellErrorForeground, _hc.Colors.CellErrorBackground, 4.5, "CellErrorForeground/CellErrorBackground");

    [TestMethod]
    public void ToolbarForeground_MeetsAA_AgainstToolbarBackground()
        => AssertContrastRatio(_hc.Colors.ToolbarForeground, _hc.Colors.ToolbarBackground, 4.5, "ToolbarForeground/ToolbarBackground");

    [TestMethod]
    public void ToolbarDisabledForeground_MeetsAA_AgainstToolbarBackground()
        => AssertContrastRatio(_hc.Colors.ToolbarDisabledForeground, _hc.Colors.ToolbarBackground, 4.5, "ToolbarDisabledForeground/ToolbarBackground");

    [TestMethod]
    public void SidebarForeground_MeetsAA_AgainstSidebarBackground()
        => AssertContrastRatio(_hc.Colors.SidebarForeground, _hc.Colors.SidebarBackground, 4.5, "SidebarForeground/SidebarBackground");

    [TestMethod]
    public void CellOutputForeground_MeetsAA_AgainstCellOutputBackground()
        => AssertContrastRatio(_hc.Colors.CellOutputForeground, _hc.Colors.CellOutputBackground, 4.5, "CellOutputForeground/CellOutputBackground");

    [TestMethod]
    public void HighlightForeground_MeetsAA_AgainstHighlightBackground()
        => AssertContrastRatio(_hc.Colors.HighlightForeground, _hc.Colors.HighlightBackground, 4.5, "HighlightForeground/HighlightBackground");

    [TestMethod]
    public void TooltipForeground_MeetsAA_AgainstTooltipBackground()
        => AssertContrastRatio(_hc.Colors.TooltipForeground, _hc.Colors.TooltipBackground, 4.5, "TooltipForeground/TooltipBackground");

    [TestMethod]
    public void EditorLineNumber_MeetsAA_AgainstEditorGutter()
        => AssertContrastRatio(_hc.Colors.EditorLineNumber, _hc.Colors.EditorGutter, 4.5, "EditorLineNumber/EditorGutter");

    [TestMethod]
    public void AllSyntaxColors_MeetEnhancedContrast_AgainstEditorBackground()
    {
        var map = _hc.GetSyntaxColors();
        var editorBg = _hc.Colors.EditorBackground;

        foreach (var kvp in map.GetAll())
        {
            AssertContrastRatio(kvp.Value, editorBg, 5.0,
                $"SyntaxColor[{kvp.Key}]/EditorBackground");
        }
    }

    // --- W3C Relative Luminance / Contrast Ratio Helpers ---

    private static void AssertContrastRatio(string hex1, string hex2, double minimumRatio, string pairName)
    {
        var ratio = CalculateContrastRatio(hex1, hex2);
        Assert.IsTrue(ratio >= minimumRatio,
            $"{pairName}: contrast ratio {ratio:F1}:1 is below required {minimumRatio}:1 " +
            $"(colors: {hex1} / {hex2})");
    }

    /// <summary>
    /// Calculates the WCAG 2.1 contrast ratio between two hex colors.
    /// Uses the W3C relative luminance formula: https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio
    /// </summary>
    private static double CalculateContrastRatio(string hex1, string hex2)
    {
        var l1 = RelativeLuminance(hex1);
        var l2 = RelativeLuminance(hex2);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Computes relative luminance per W3C WCAG 2.1 specification.
    /// </summary>
    private static double RelativeLuminance(string hex)
    {
        var r = SrgbToLinear(int.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber) / 255.0);
        var g = SrgbToLinear(int.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber) / 255.0);
        var b = SrgbToLinear(int.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber) / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>
    /// Converts an sRGB channel value (0-1) to linear RGB.
    /// </summary>
    private static double SrgbToLinear(double channel)
        => channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
