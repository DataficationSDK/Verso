using Microsoft.AspNetCore.Components.Forms;

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

    // ── Add Cell buttons ───────────────────────────────────────────────

    [TestMethod]
    public void AddCodeAndMarkdown_ShownWhenLoaded()
    {
        _service.IsLoaded = true;

        var cut = RenderToolbar();

        Assert.IsTrue(cut.Markup.Contains("+ Code"));
        Assert.IsTrue(cut.Markup.Contains("+ Markdown"));
    }

    [TestMethod]
    public void AddCodeButton_FiresOnAddCell_WithCode()
    {
        string? addedType = null;
        var cut = RenderToolbar(onAddCell: type => addedType = type);

        var btn = cut.FindAll("button").First(b => b.TextContent.Contains("+ Code"));
        btn.Click();

        Assert.AreEqual("code", addedType);
    }

    [TestMethod]
    public void AddMarkdownButton_FiresOnAddCell_WithMarkdown()
    {
        string? addedType = null;
        var cut = RenderToolbar(onAddCell: type => addedType = type);

        var btn = cut.FindAll("button").First(b => b.TextContent.Contains("+ Markdown"));
        btn.Click();

        Assert.AreEqual("markdown", addedType);
    }

    // ── Layout dropdown ────────────────────────────────────────────────

    [TestMethod]
    public void LayoutDropdown_HiddenWhenSingleLayout()
    {
        _service.AvailableLayouts = new List<LayoutInfo>
        {
            new("notebook", "Notebook", false)
        };

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Markup.Contains("Switch Layout"));
    }

    [TestMethod]
    public void LayoutDropdown_ShownWhenMultipleLayouts()
    {
        _service.AvailableLayouts = new List<LayoutInfo>
        {
            new("notebook", "Notebook", false),
            new("dashboard", "Dashboard", true)
        };
        _service.ActiveLayoutId = "notebook";

        var cut = RenderToolbar();

        Assert.IsTrue(cut.Markup.Contains("Notebook"));
    }

    // ── Theme dropdown ─────────────────────────────────────────────────

    [TestMethod]
    public void ThemeDropdown_HiddenWhenSingleTheme()
    {
        _service.AvailableThemes = new List<ThemeInfo>
        {
            new("light", "Light", ThemeKind.Light)
        };

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Markup.Contains("Switch Theme"));
    }

    [TestMethod]
    public void ThemeDropdown_ShownWhenMultipleThemes_NotEmbedded()
    {
        _service.AvailableThemes = new List<ThemeInfo>
        {
            new("light", "Light", ThemeKind.Light),
            new("dark", "Dark", ThemeKind.Dark)
        };
        _service.ActiveThemeId = "light";
        _service.IsEmbedded = false;

        var cut = RenderToolbar();

        Assert.IsTrue(cut.Markup.Contains("Light"));
    }

    [TestMethod]
    public void ThemeDropdown_HiddenWhenEmbedded()
    {
        _service.AvailableThemes = new List<ThemeInfo>
        {
            new("light", "Light", ThemeKind.Light),
            new("dark", "Dark", ThemeKind.Dark)
        };
        _service.IsEmbedded = true;

        var cut = RenderToolbar();

        // Theme dropdown hidden in embedded mode
        Assert.IsFalse(cut.Markup.Contains("Switch Theme"));
    }

    // ── Not loaded state ───────────────────────────────────────────────

    [TestMethod]
    public void WhenNotLoaded_CellAndSaveButtonsHidden()
    {
        _service.IsLoaded = false;

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Markup.Contains("Save"));
        Assert.IsFalse(cut.Markup.Contains("+ Code"));
        Assert.IsFalse(cut.Markup.Contains("+ Markdown"));
    }

    // ── Helper ─────────────────────────────────────────────────────────

    private IRenderedComponent<Toolbar> RenderToolbar(
        Action<string>? onAddCell = null)
    {
        return RenderComponent<Toolbar>(p => p
            .Add(t => t.Service, _service)
            .Add(t => t.OnAddCell, onAddCell ?? (_ => { })));
    }
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
