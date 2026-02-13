using Verso.Abstractions;
using Verso.Extensions.Themes;

namespace Verso.Tests;

[TestClass]
public sealed class ThemeEngineTests
{
    [TestMethod]
    public void SetActiveTheme_SetsThemeByThemeId()
    {
        var light = new VersoLightTheme();
        var dark = new VersoDarkTheme();
        var engine = new ThemeEngine(new ITheme[] { light, dark });

        engine.SetActiveTheme("verso-light");
        Assert.AreSame(light, engine.ActiveTheme);

        engine.SetActiveTheme("verso-dark");
        Assert.AreSame(dark, engine.ActiveTheme);
    }

    [TestMethod]
    public void SetActiveTheme_UnknownId_Throws()
    {
        var engine = new ThemeEngine(new ITheme[] { new VersoLightTheme() });
        Assert.ThrowsException<InvalidOperationException>(() => engine.SetActiveTheme("nonexistent"));
    }

    [TestMethod]
    public void Constructor_WithDefaultThemeId_SetsActiveTheme()
    {
        var light = new VersoLightTheme();
        var engine = new ThemeEngine(new ITheme[] { light }, "verso-light");

        Assert.AreSame(light, engine.ActiveTheme);
    }

    [TestMethod]
    public void ThemeKind_ReflectsActiveTheme()
    {
        var light = new VersoLightTheme();
        var dark = new VersoDarkTheme();
        var engine = new ThemeEngine(new ITheme[] { light, dark });

        // No theme active — defaults to Light
        Assert.AreEqual(ThemeKind.Light, engine.ThemeKind);

        engine.SetActiveTheme("verso-dark");
        Assert.AreEqual(ThemeKind.Dark, engine.ThemeKind);
    }

    [TestMethod]
    public void GetColor_ResolvesFromActiveTheme()
    {
        var light = new VersoLightTheme();
        var dark = new VersoDarkTheme();
        var engine = new ThemeEngine(new ITheme[] { light, dark }, "verso-light");

        Assert.AreEqual("#FFFFFF", engine.GetColor("EditorBackground"));

        engine.SetActiveTheme("verso-dark");
        Assert.AreEqual("#1E1E1E", engine.GetColor("EditorBackground"));
    }

    [TestMethod]
    public void GetColor_Fallback_WhenNoTheme()
    {
        var engine = new ThemeEngine(Array.Empty<ITheme>());
        // Should return default color from ThemeColorTokens
        var color = engine.GetColor("EditorBackground");
        Assert.AreEqual("#FFFFFF", color);
    }

    [TestMethod]
    public void GetColor_UnknownToken_ReturnsEmpty()
    {
        var engine = new ThemeEngine(new ITheme[] { new VersoLightTheme() }, "verso-light");
        Assert.AreEqual("", engine.GetColor("NonExistentToken"));
    }

    [TestMethod]
    public void GetFont_ResolvesFromActiveTheme()
    {
        var light = new VersoLightTheme();
        var engine = new ThemeEngine(new ITheme[] { light }, "verso-light");

        var font = engine.GetFont("EditorFont");
        Assert.AreEqual("Cascadia Code", font.Family);
        Assert.AreEqual(14, font.SizePx);
    }

    [TestMethod]
    public void GetFont_Fallback_WhenNoTheme()
    {
        var engine = new ThemeEngine(Array.Empty<ITheme>());
        var font = engine.GetFont("EditorFont");
        Assert.AreEqual("Cascadia Code", font.Family);
    }

    [TestMethod]
    public void GetFont_UnknownRole_ReturnsSansSerifDefault()
    {
        var engine = new ThemeEngine(new ITheme[] { new VersoLightTheme() }, "verso-light");
        var font = engine.GetFont("UnknownFontRole");
        Assert.AreEqual("sans-serif", font.Family);
        Assert.AreEqual(13, font.SizePx);
    }

    [TestMethod]
    public void GetSpacing_ResolvesFromActiveTheme()
    {
        var light = new VersoLightTheme();
        var engine = new ThemeEngine(new ITheme[] { light }, "verso-light");

        var spacing = engine.GetSpacing("CellPadding");
        Assert.AreEqual(12, spacing);
    }

    [TestMethod]
    public void GetSpacing_Fallback_WhenNoTheme()
    {
        var engine = new ThemeEngine(Array.Empty<ITheme>());
        Assert.AreEqual(12, engine.GetSpacing("CellPadding"));
    }

    [TestMethod]
    public void GetSyntaxColor_ResolvesFromActiveTheme()
    {
        var light = new VersoLightTheme();
        var engine = new ThemeEngine(new ITheme[] { light }, "verso-light");

        Assert.AreEqual("#0000FF", engine.GetSyntaxColor("keyword"));
    }

    [TestMethod]
    public void GetSyntaxColor_NullWhenNoTheme()
    {
        var engine = new ThemeEngine(Array.Empty<ITheme>());
        Assert.IsNull(engine.GetSyntaxColor("keyword"));
    }

    [TestMethod]
    public void GetCustomToken_DelegatesToActiveTheme()
    {
        var engine = new ThemeEngine(new ITheme[] { new VersoLightTheme() }, "verso-light");
        // VersoLightTheme returns null for all custom tokens
        Assert.IsNull(engine.GetCustomToken("anyKey"));
    }

    [TestMethod]
    public void GetCustomToken_NullWhenNoTheme()
    {
        var engine = new ThemeEngine(Array.Empty<ITheme>());
        Assert.IsNull(engine.GetCustomToken("anyKey"));
    }

    [TestMethod]
    public void SwitchBetweenThemes_ChangesBehavior()
    {
        var light = new VersoLightTheme();
        var dark = new VersoDarkTheme();
        var engine = new ThemeEngine(new ITheme[] { light, dark }, "verso-light");

        Assert.AreEqual(ThemeKind.Light, engine.ThemeKind);
        Assert.AreEqual("#0000FF", engine.GetSyntaxColor("keyword"));

        engine.SetActiveTheme("verso-dark");

        Assert.AreEqual(ThemeKind.Dark, engine.ThemeKind);
        Assert.AreEqual("#569CD6", engine.GetSyntaxColor("keyword"));
    }

    [TestMethod]
    public void AvailableThemes_ReturnsAll()
    {
        var themes = new ITheme[] { new VersoLightTheme(), new VersoDarkTheme() };
        var engine = new ThemeEngine(themes);
        Assert.AreEqual(2, engine.AvailableThemes.Count);
    }
}
