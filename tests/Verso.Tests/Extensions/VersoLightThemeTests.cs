using System.Reflection;
using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Extensions.Themes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class VersoLightThemeTests
{
    private readonly VersoLightTheme _theme = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.theme.light", _theme.ExtensionId);

    [TestMethod]
    public void ThemeKind_IsLight()
        => Assert.AreEqual(ThemeKind.Light, _theme.ThemeKind);

    [TestMethod]
    public void Colors_MatchDefaults()
    {
        var defaults = new ThemeColorTokens();
        Assert.AreEqual(defaults.EditorBackground, _theme.Colors.EditorBackground);
        Assert.AreEqual(defaults.EditorForeground, _theme.Colors.EditorForeground);
        Assert.AreEqual(defaults.CellBackground, _theme.Colors.CellBackground);
    }

    [TestMethod]
    public void Typography_MatchesDefaults()
    {
        var defaults = new ThemeTypography();
        Assert.AreEqual(defaults.EditorFont, _theme.Typography.EditorFont);
        Assert.AreEqual(defaults.UIFont, _theme.Typography.UIFont);
    }

    [TestMethod]
    public void Spacing_MatchesDefaults()
    {
        var defaults = new ThemeSpacing();
        Assert.AreEqual(defaults.CellPadding, _theme.Spacing.CellPadding);
        Assert.AreEqual(defaults.CellGap, _theme.Spacing.CellGap);
    }

    [TestMethod]
    public void AllColorTokens_AreValidHex()
    {
        var hexRegex = new Regex(@"^#[0-9A-Fa-f]{6}$");
        var props = typeof(ThemeColorTokens)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string));

        foreach (var prop in props)
        {
            var value = (string)prop.GetValue(_theme.Colors)!;
            Assert.IsTrue(hexRegex.IsMatch(value),
                $"Color token {prop.Name} has invalid hex value: {value}");
        }
    }

    [TestMethod]
    public void SyntaxColors_HasAtLeast12Tokens()
    {
        var map = _theme.GetSyntaxColors();
        Assert.IsTrue(map.Count >= 12, $"Expected >=12 syntax colors, got {map.Count}");
    }

    [TestMethod]
    public void SyntaxColors_ContainsExpectedKeys()
    {
        var map = _theme.GetSyntaxColors();
        Assert.IsNotNull(map.Get("keyword"));
        Assert.IsNotNull(map.Get("comment"));
        Assert.IsNotNull(map.Get("string"));
        Assert.IsNotNull(map.Get("number"));
        Assert.IsNotNull(map.Get("type"));
        Assert.IsNotNull(map.Get("function"));
    }

    [TestMethod]
    public void SyntaxColors_AreValidHex()
    {
        var hexRegex = new Regex(@"^#[0-9A-Fa-f]{6}$");
        var map = _theme.GetSyntaxColors();

        foreach (var kvp in map.GetAll())
        {
            Assert.IsTrue(hexRegex.IsMatch(kvp.Value),
                $"Syntax color {kvp.Key} has invalid hex value: {kvp.Value}");
        }
    }
}
