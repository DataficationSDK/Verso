namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class ThemeProviderTests : BunitTestContext
{
    [TestInitialize]
    public void EnableLooseJSInterop()
    {
        // ThemeProvider invokes "verso.theme.dispatchChange" from OnAfterRenderAsync so
        // bridge-subscribed scripts get notified when the theme actually changes. Tests
        // here only assert on the rendered <style> contents, so accept any JS call.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;

        // ThemeProvider injects INotebookService to read the active theme kind for the
        // dispatched change event; register a fake so the component can be constructed.
        TestContext!.Services.AddSingleton<INotebookService>(new FakeNotebookService());
    }

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

    [TestMethod]
    public void LayoutExtensionPalette_AllNineTokensEmitted()
    {
        var themeData = CreateDefaultThemeData();

        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, themeData));

        var style = cut.Find("style").TextContent;

        // Coarse semantic palette consumed by layout extensions via var(--verso-bg-default) etc.
        Assert.IsTrue(style.Contains("--verso-bg-default:"), "missing --verso-bg-default");
        Assert.IsTrue(style.Contains("--verso-bg-elevated:"), "missing --verso-bg-elevated");
        Assert.IsTrue(style.Contains("--verso-fg-default:"), "missing --verso-fg-default");
        Assert.IsTrue(style.Contains("--verso-fg-muted:"), "missing --verso-fg-muted");
        Assert.IsTrue(style.Contains("--verso-border-default:"), "missing --verso-border-default");
        Assert.IsTrue(style.Contains("--verso-accent:"), "missing --verso-accent");
        Assert.IsTrue(style.Contains("--verso-font-family-mono:"), "missing --verso-font-family-mono");
        Assert.IsTrue(style.Contains("--verso-font-family-sans:"), "missing --verso-font-family-sans");
        Assert.IsTrue(style.Contains("--verso-font-size-base:"), "missing --verso-font-size-base");
    }

    [TestMethod]
    public void TypographyLoop_StillEmitsFontDescriptorTokens()
    {
        // Regression: the typography loop was generalized to also accept string/double
        // properties; FontDescriptor-shaped tokens must still emit the four-part shape.
        var themeData = CreateDefaultThemeData();

        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, themeData));

        var style = cut.Find("style").TextContent;

        Assert.IsTrue(style.Contains("--verso-editor-font-family:"));
        Assert.IsTrue(style.Contains("--verso-editor-font-size:"));
        Assert.IsTrue(style.Contains("--verso-editor-font-weight:"));
        Assert.IsTrue(style.Contains("--verso-editor-font-line-height:"));
    }

    [TestMethod]
    public void FontSizeBase_EmittedWithPxUnit()
    {
        var themeData = new ThemeData(
            new ThemeColorTokens(),
            new ThemeTypography { FontSizeBase = 16.0 },
            new ThemeSpacing());

        var cut = RenderComponent<ThemeProvider>(p => p
            .Add(t => t.Theme, themeData));

        var style = cut.Find("style").TextContent;

        Assert.IsTrue(style.Contains("--verso-font-size-base: 16px;"));
    }

    private static ThemeData CreateDefaultThemeData()
    {
        return new ThemeData(
            new ThemeColorTokens(),
            new ThemeTypography(),
            new ThemeSpacing());
    }
}
