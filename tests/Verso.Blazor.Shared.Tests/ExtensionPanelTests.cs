namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class ExtensionPanelTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService { IsLoaded = true };
    }

    [TestMethod]
    public void NotLoaded_HiddenOrEmpty()
    {
        _service.IsLoaded = false;

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        Assert.IsFalse(cut.Markup.Contains("verso-extension-group"));
    }

    [TestMethod]
    public void WithExtensions_ShowsCategoryGroups()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.sql", "SQL Kernel", "1.0.0", "Author", "SQL support", ExtensionStatus.Enabled, new[] { "LanguageKernel" }),
            new("ext.theme", "Dark Theme", "2.1.0", null, "Dark mode", ExtensionStatus.Enabled, new[] { "Theme" })
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        // Groups should be visible (collapsed)
        Assert.IsTrue(cut.Markup.Contains("Language Kernels"));
        Assert.IsTrue(cut.Markup.Contains("Themes"));
    }

    [TestMethod]
    public void ClickCategory_ExpandsAndShowsExtensionDetails()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.sql", "SQL Kernel", "1.0.0", "Author", "SQL support", ExtensionStatus.Enabled, new[] { "LanguageKernel" })
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        // Click the category header to expand
        var header = cut.Find(".verso-extension-group-header");
        header.Click();

        // Now the extension details should be visible
        Assert.IsTrue(cut.Markup.Contains("SQL Kernel"));
        Assert.IsTrue(cut.Markup.Contains("1.0.0"));
    }

    [TestMethod]
    public void NoExtensions_ShowsEmptyState()
    {
        _service.Extensions = new List<ExtensionInfo>();

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        Assert.IsFalse(cut.Markup.Contains("verso-extension-group"));
    }

    [TestMethod]
    public void ExpandedCategory_ShowsToggleButton()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.a", "ExtA", "1.0.0", null, null, ExtensionStatus.Enabled, new[] { "LanguageKernel" })
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        // Expand the category
        cut.Find(".verso-extension-group-header").Click();

        // Should show enable/disable toggle
        var toggleBtn = cut.Find(".verso-extension-toggle");
        Assert.IsNotNull(toggleBtn);
        Assert.IsTrue(toggleBtn.TextContent.Contains("On"));
    }

    [TestMethod]
    public void DisabledExtension_ShowsOffToggle()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.b", "ExtB", "1.0.0", null, null, ExtensionStatus.Disabled, new[] { "Theme" })
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        cut.Find(".verso-extension-group-header").Click();

        var toggleBtn = cut.Find(".verso-extension-toggle");
        Assert.IsTrue(toggleBtn.TextContent.Contains("Off"));
    }

    [TestMethod]
    public void MultipleExtensions_GroupedByCategory()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.1", "First", "1.0", null, null, ExtensionStatus.Enabled, new[] { "LanguageKernel" }),
            new("ext.2", "Second", "2.0", null, null, ExtensionStatus.Disabled, new[] { "Theme" }),
            new("ext.3", "Third", "3.0", null, null, ExtensionStatus.Enabled, new[] { "LanguageKernel" })
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        // Should have 2 category groups
        var groups = cut.FindAll(".verso-extension-group");
        Assert.AreEqual(2, groups.Count);
    }

    [TestMethod]
    public void ExpandedExtension_ShowsAuthorAndDescription()
    {
        _service.Extensions = new List<ExtensionInfo>
        {
            new("ext.sql", "SQL Kernel", "1.0.0", "John Doe", "Adds SQL support", ExtensionStatus.Enabled, new[] { "LanguageKernel" })
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        cut.Find(".verso-extension-group-header").Click();

        Assert.IsTrue(cut.Markup.Contains("John Doe"));
        Assert.IsTrue(cut.Markup.Contains("Adds SQL support"));
    }
}
