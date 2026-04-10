namespace MyExtension.Tests;

[TestClass]
public sealed class SampleThemeTests
{
    private readonly SampleTheme _theme = new();

    [TestMethod]
    public void ExtensionId_IsSet()
    {
        Assert.IsFalse(string.IsNullOrEmpty(_theme.ExtensionId));
    }

    [TestMethod]
    public void ThemeId_IsSet()
    {
        Assert.IsFalse(string.IsNullOrEmpty(_theme.ThemeId));
    }

    [TestMethod]
    public void ThemeKind_IsDark()
    {
        Assert.AreEqual(ThemeKind.Dark, _theme.ThemeKind);
    }

    [TestMethod]
    public void Colors_HasNonDefaultBackground()
    {
        Assert.AreEqual("#1e1e2e", _theme.Colors.EditorBackground);
    }

    [TestMethod]
    public void GetSyntaxColors_ContainsKeyword()
    {
        var colors = _theme.GetSyntaxColors();
        Assert.IsNotNull(colors.Get("keyword"));
    }

    [TestMethod]
    public void GetCustomToken_ReturnsNull()
    {
        Assert.IsNull(_theme.GetCustomToken("nonexistent"));
    }
}
