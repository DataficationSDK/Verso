namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class ThemeProviderTests : BunitTestContext
{
    [TestMethod]
    public void RendersStyleElement_WithCssVariables()
    {
        var themeData = CreateDefaultThemeData();

        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, themeData));

        var style = cut.Find("style");
        Assert.IsNotNull(style);
        Assert.IsTrue(style.TextContent.Contains("--verso-"));
    }

    [TestMethod]
    public void ColorTokens_PresentInStyle()
    {
        var themeData = CreateDefaultThemeData();

        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, themeData));

        var style = cut.Find("style").TextContent;

        Assert.IsTrue(style.Contains("--verso-editor-background"));
        Assert.IsTrue(style.Contains("--verso-editor-foreground"));
        Assert.IsTrue(style.Contains("--verso-cell-background"));
        Assert.IsTrue(style.Contains("--verso-cell-border"));
        Assert.IsTrue(style.Contains("--verso-toolbar-background"));
    }

    [TestMethod]
    public void TypographyTokens_PresentInStyle()
    {
        var themeData = CreateDefaultThemeData();

        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, themeData));

        var style = cut.Find("style").TextContent;

        // ThemeTypography props: EditorFont → editor-font, UIFont → u-i-font
        Assert.IsTrue(style.Contains("-font-family"));
        Assert.IsTrue(style.Contains("-font-size"));
        Assert.IsTrue(style.Contains("-font-weight"));
    }

    [TestMethod]
    public void SpacingTokens_PresentInStyle()
    {
        var themeData = CreateDefaultThemeData();

        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, themeData));

        var style = cut.Find("style").TextContent;

        Assert.IsTrue(style.Contains("--verso-cell-padding"));
        Assert.IsTrue(style.Contains("--verso-cell-gap"));
        Assert.IsTrue(style.Contains("--verso-toolbar-height"));
    }

    [TestMethod]
    public void NullTheme_RendersEmptyOrDefault()
    {
        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, null));

        // When no theme data, the component should still render without errors
        Assert.IsNotNull(cut.Markup);
    }

    [TestMethod]
    public void CustomColors_ReflectedInOutput()
    {
        var colors = new ThemeColorTokens
        {
            EditorBackground = "#1E1E1E",
            EditorForeground = "#D4D4D4"
        };
        var themeData = new ThemeData(colors, new ThemeTypography(), new ThemeSpacing());

        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, themeData));

        var style = cut.Find("style").TextContent;
        Assert.IsTrue(style.Contains("#1E1E1E"));
        Assert.IsTrue(style.Contains("#D4D4D4"));
    }

    [TestMethod]
    public void AllColorProperties_GenerateCssVariables()
    {
        var themeData = CreateDefaultThemeData();

        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, themeData));

        var style = cut.Find("style").TextContent;

        // Verify a representative sample of color tokens are generated
        Assert.IsTrue(style.Contains("--verso-cell-active-border"));
        Assert.IsTrue(style.Contains("--verso-accent-primary"));
        Assert.IsTrue(style.Contains("--verso-status-error"));
        Assert.IsTrue(style.Contains("--verso-sidebar-bg") || style.Contains("--verso-sidebar-background"));
    }

    [TestMethod]
    public void CssVariables_InRootSelector()
    {
        var themeData = CreateDefaultThemeData();

        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, themeData));

        var style = cut.Find("style").TextContent;

        Assert.IsTrue(style.Contains(":root"));
    }

    private static ThemeData CreateDefaultThemeData()
    {
        return new ThemeData(
            new ThemeColorTokens(),
            new ThemeTypography(),
            new ThemeSpacing());
    }
}
