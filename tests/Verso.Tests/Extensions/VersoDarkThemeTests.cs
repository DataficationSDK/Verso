using System.Reflection;
using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Extensions.Themes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class VersoDarkThemeTests
{
    private readonly VersoDarkTheme _dark = new();
    private readonly VersoLightTheme _light = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.theme.dark", _dark.ExtensionId);

    [TestMethod]
    public void ThemeKind_IsDark()
        => Assert.AreEqual(ThemeKind.Dark, _dark.ThemeKind);

    [TestMethod]
    public void AllColorTokens_AreValidHex()
    {
        var hexRegex = new Regex(@"^#[0-9A-Fa-f]{6}$");
        var props = typeof(ThemeColorTokens)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string));

        foreach (var prop in props)
        {
            var value = (string)prop.GetValue(_dark.Colors)!;
            Assert.IsTrue(hexRegex.IsMatch(value),
                $"Color token {prop.Name} has invalid hex value: {value}");
        }
    }

    [TestMethod]
    public void Colors_DifferFromLight()
    {
        Assert.AreNotEqual(_light.Colors.EditorBackground, _dark.Colors.EditorBackground);
        Assert.AreNotEqual(_light.Colors.EditorForeground, _dark.Colors.EditorForeground);
        Assert.AreNotEqual(_light.Colors.CellBackground, _dark.Colors.CellBackground);
    }

    [TestMethod]
    public void Typography_SameAsLight()
    {
        Assert.AreEqual(_light.Typography.EditorFont, _dark.Typography.EditorFont);
        Assert.AreEqual(_light.Typography.UIFont, _dark.Typography.UIFont);
        Assert.AreEqual(_light.Typography.ProseFont, _dark.Typography.ProseFont);
    }

    [TestMethod]
    public void Spacing_SameAsLight()
    {
        Assert.AreEqual(_light.Spacing.CellPadding, _dark.Spacing.CellPadding);
        Assert.AreEqual(_light.Spacing.CellGap, _dark.Spacing.CellGap);
    }

    [TestMethod]
    public void SyntaxColors_HasAtLeast12Tokens()
    {
        var map = _dark.GetSyntaxColors();
        Assert.IsTrue(map.Count >= 12, $"Expected >=12 syntax colors, got {map.Count}");
    }

    [TestMethod]
    public void SyntaxColors_DifferFromLight()
    {
        var darkMap = _dark.GetSyntaxColors();
        var lightMap = _light.GetSyntaxColors();
        Assert.AreNotEqual(lightMap.Get("keyword"), darkMap.Get("keyword"));
        Assert.AreNotEqual(lightMap.Get("comment"), darkMap.Get("comment"));
        Assert.AreNotEqual(lightMap.Get("string"), darkMap.Get("string"));
    }

    [TestMethod]
    public void SyntaxColors_AreValidHex()
    {
        var hexRegex = new Regex(@"^#[0-9A-Fa-f]{6}$");
        var map = _dark.GetSyntaxColors();

        foreach (var kvp in map.GetAll())
        {
            Assert.IsTrue(hexRegex.IsMatch(kvp.Value),
                $"Syntax color {kvp.Key} has invalid hex value: {kvp.Value}");
        }
    }
}
