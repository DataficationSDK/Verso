using System.Reflection;
using System.Text.RegularExpressions;

namespace Verso.Abstractions.Tests.Theming;

[TestClass]
public class ThemeRecordTests
{
    private static readonly Regex HexColorPattern = new(@"^#[0-9A-Fa-f]{6}$");

    [TestMethod]
    public void ThemeColorTokens_AllDefaults_AreNonNullValidHex()
    {
        var tokens = new ThemeColorTokens();
        var props = typeof(ThemeColorTokens).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            var value = (string?)prop.GetValue(tokens);
            Assert.IsNotNull(value, $"{prop.Name} should not be null");
            Assert.IsTrue(HexColorPattern.IsMatch(value!), $"{prop.Name} value '{value}' is not valid hex (#RRGGBB)");
        }
    }

    [TestMethod]
    public void ThemeColorTokens_HasExpectedPropertyCount()
    {
        var count = typeof(ThemeColorTokens).GetProperties(BindingFlags.Public | BindingFlags.Instance).Length;
        Assert.IsTrue(count >= 40, $"Expected at least 40 color tokens, found {count}");
    }

    [TestMethod]
    public void ThemeTypography_AllDefaults_AreNonNull()
    {
        var typo = new ThemeTypography();
        var props = typeof(ThemeTypography).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            var value = prop.GetValue(typo);
            Assert.IsNotNull(value, $"{prop.Name} should not be null");
        }
    }

    [TestMethod]
    public void ThemeTypography_HeadingSizes_Descend()
    {
        var typo = new ThemeTypography();
        var sizes = new[]
        {
            typo.H1Font.SizePx,
            typo.H2Font.SizePx,
            typo.H3Font.SizePx,
            typo.H4Font.SizePx,
            typo.H5Font.SizePx,
            typo.H6Font.SizePx
        };

        for (int i = 1; i < sizes.Length; i++)
        {
            Assert.IsTrue(sizes[i - 1] > sizes[i],
                $"H{i} ({sizes[i - 1]}px) should be larger than H{i + 1} ({sizes[i]}px)");
        }
    }

    [TestMethod]
    public void ThemeSpacing_AllValues_ArePositive()
    {
        var spacing = new ThemeSpacing();
        var props = typeof(ThemeSpacing).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            var value = (double)prop.GetValue(spacing)!;
            Assert.IsTrue(value > 0, $"{prop.Name} should be positive, was {value}");
        }
    }

    [TestMethod]
    public void ThemeColorTokens_WithExpression_Overrides()
    {
        var tokens = new ThemeColorTokens { EditorBackground = "#000000" };
        Assert.AreEqual("#000000", tokens.EditorBackground);
        Assert.AreEqual("#1E1E1E", tokens.EditorForeground); // other defaults intact
    }

    [TestMethod]
    public void ThemeSpacing_WithExpression_Overrides()
    {
        var spacing = new ThemeSpacing { CellPadding = 20 };
        Assert.AreEqual(20, spacing.CellPadding);
        Assert.AreEqual(8, spacing.CellGap); // other defaults intact
    }

    [TestMethod]
    public void ThemeTypography_WithExpression_Overrides()
    {
        var typo = new ThemeTypography { EditorFont = new FontDescriptor("Fira Code", 16) };
        Assert.AreEqual("Fira Code", typo.EditorFont.Family);
        Assert.AreEqual(16, typo.EditorFont.SizePx);
        Assert.AreEqual("Segoe UI", typo.UIFont.Family); // other defaults intact
    }
}
