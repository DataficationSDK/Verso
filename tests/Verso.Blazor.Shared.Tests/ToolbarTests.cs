namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class ToolbarTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService { IsLoaded = true, IsEmbedded = false };
    }

    // ── Visibility ─────────────────────────────────────────────────────

    [TestMethod]
    public void NewAndOpen_HiddenWhenEmbedded()
    {
        _service.IsEmbedded = true;

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Markup.Contains("New"));
        Assert.IsFalse(cut.Markup.Contains("Open"));
    }

    [TestMethod]
    public void NewAndOpen_ShownWhenNotEmbedded()
    {
        _service.IsEmbedded = false;

        var cut = RenderToolbar();

        Assert.IsTrue(cut.Markup.Contains("New"));
        Assert.IsTrue(cut.Markup.Contains("Open"));
    }

    [TestMethod]
    public void Save_ShownWhenLoaded()
    {
        _service.IsLoaded = true;

        var cut = RenderToolbar();

        Assert.IsTrue(cut.Markup.Contains("Save"));
    }

    [TestMethod]
    public void Save_HiddenWhenNotLoaded()
    {
        _service.IsLoaded = false;

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Markup.Contains("Save"));
    }

    // ── View dropdown (Layout + Theme) ─────────────────────────────────
    //
    // Layout and theme switching share a single "View" dropdown. The button appears only when
    // there is something to switch (more than one layout, or more than one theme on a host that
    // owns its own theming), and each section renders only when its own choices warrant it. The
    // choices live inside the popup, so the dropdown is opened before asserting on them.

    [TestMethod]
    public void ViewDropdown_HiddenWhenSingleLayoutAndSingleTheme()
    {
        _service.AvailableLayouts = new List<LayoutInfo> { new("notebook", "Notebook", false) };
        _service.AvailableThemes = new List<ThemeInfo> { new("light", "Light", ThemeKind.Light) };

        var cut = RenderToolbar();

        Assert.IsFalse(HasViewButton(cut));
    }

    [TestMethod]
    public void ViewDropdown_ShowsLayouts_WhenMultipleLayouts()
    {
        _service.AvailableLayouts = new List<LayoutInfo>
        {
            new("notebook", "Notebook", false),
            new("dashboard", "Dashboard", true)
        };
        _service.ActiveLayoutId = "notebook";

        var cut = RenderToolbar();
        OpenViewDropdown(cut);

        Assert.IsTrue(cut.Markup.Contains("Layout"));      // section header
        Assert.IsTrue(cut.Markup.Contains("Dashboard"));   // the switchable layout
    }

    [TestMethod]
    public void ViewDropdown_OmitsLayoutSection_WhenSingleLayout()
    {
        // One layout, but multiple themes force the dropdown open: it offers themes and no
        // Layout section, since there is nothing to switch to.
        _service.AvailableLayouts = new List<LayoutInfo> { new("notebook", "Notebook", false) };
        _service.AvailableThemes = new List<ThemeInfo>
        {
            new("light", "Light", ThemeKind.Light),
            new("dark", "Dark", ThemeKind.Dark)
        };

        var cut = RenderToolbar();
        OpenViewDropdown(cut);

        Assert.IsFalse(cut.Markup.Contains("Layout"));
        Assert.IsTrue(cut.Markup.Contains("Theme"));
    }

    [TestMethod]
    public void ViewDropdown_ShowsThemes_WhenMultipleThemes_NotEmbedded()
    {
        _service.AvailableThemes = new List<ThemeInfo>
        {
            new("light", "Light", ThemeKind.Light),
            new("dark", "Dark", ThemeKind.Dark)
        };
        _service.ActiveThemeId = "light";
        _service.IsEmbedded = false;

        var cut = RenderToolbar();
        OpenViewDropdown(cut);

        Assert.IsTrue(cut.Markup.Contains("Theme"));   // section header
        Assert.IsTrue(cut.Markup.Contains("Dark"));    // the switchable theme
    }

    [TestMethod]
    public void ViewDropdown_OmitsThemeSection_WhenSingleTheme()
    {
        // Multiple layouts force the dropdown open; one theme means no Theme section.
        _service.AvailableLayouts = new List<LayoutInfo>
        {
            new("notebook", "Notebook", false),
            new("dashboard", "Dashboard", true)
        };
        _service.AvailableThemes = new List<ThemeInfo> { new("light", "Light", ThemeKind.Light) };

        var cut = RenderToolbar();
        OpenViewDropdown(cut);

        Assert.IsFalse(cut.Markup.Contains("Theme"));
        Assert.IsTrue(cut.Markup.Contains("Layout"));
    }

    [TestMethod]
    public void ViewDropdown_OmitsThemeSection_WhenEmbedded()
    {
        // Embedded hosts own theming, so the Theme section never appears even with many themes.
        _service.AvailableLayouts = new List<LayoutInfo>
        {
            new("notebook", "Notebook", false),
            new("dashboard", "Dashboard", true)
        };
        _service.AvailableThemes = new List<ThemeInfo>
        {
            new("light", "Light", ThemeKind.Light),
            new("dark", "Dark", ThemeKind.Dark)
        };
        _service.IsEmbedded = true;

        var cut = RenderToolbar();
        OpenViewDropdown(cut);

        Assert.IsFalse(cut.Markup.Contains("Theme"));
    }

    // ── Not loaded state ───────────────────────────────────────────────

    [TestMethod]
    public void WhenNotLoaded_SaveButtonHidden()
    {
        _service.IsLoaded = false;

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Markup.Contains("Save"));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private IRenderedComponent<Toolbar> RenderToolbar()
        => RenderComponent<Toolbar>(p => p.Add(t => t.Service, _service));

    private static bool HasViewButton(IRenderedComponent<Toolbar> cut)
        => cut.FindAll("button").Any(b => b.TextContent.Contains("View"));

    private static void OpenViewDropdown(IRenderedComponent<Toolbar> cut)
        => cut.FindAll("button").First(b => b.TextContent.Contains("View")).Click();
}

/// <summary>
/// Base class providing a bUnit <see cref="TestContext"/> scoped to each test.
/// </summary>
public abstract class BunitTestContext : TestContextWrapper
{
    [TestInitialize]
    public void BunitSetup() => TestContext = new Bunit.TestContext();

    [TestCleanup]
    public void BunitTeardown() => TestContext?.Dispose();
}
