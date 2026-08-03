using Verso.Blazor.Shared.Resources;

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
        Assert.IsTrue(cut.Markup.Contains(UI.Capability_LanguageKernel_Plural));
        Assert.IsTrue(cut.Markup.Contains(UI.Capability_Theme_Plural));
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
    public void UnavailableInstalledExtension_StatesTheReasonOnTheRow()
    {
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Verso.Showcase.FormStudio", "1.0.0", false, "the extension is not installed on this machine")
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        // The reason is read straight off the row rather than being hidden behind a hover,
        // which is the point of putting state on the row.
        var note = cut.Find(".verso-marketplace-row-note");
        Assert.IsTrue(note.TextContent.Contains("not installed on this machine"));
        Assert.IsTrue(cut.Markup.Contains("verso-marketplace-row--error"));
    }

    [TestMethod]
    public void AvailableInstalledExtension_HasNoWarning()
    {
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Verso.Showcase.FormStudio", "1.0.0", false)
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        Assert.IsFalse(cut.Markup.Contains("verso-marketplace-row-note"));
        Assert.IsTrue(cut.Markup.Contains("verso-marketplace-row--installed"));
    }

    // --- One list, install state on the row ---

    [TestMethod]
    public void InstalledPackage_AlsoInSearchResults_ListedOnce()
    {
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Verso.Showcase.Dag", "1.2.0", false)
        };
        _service.SearchResults = new List<PackageSearchResultDto>
        {
            new("Verso.Showcase.Dag", "1.2.0", "Dependency graph notebook.", "Datafication", 4200, null, null, true)
        };

        var cut = RenderSearch("dag");

        Assert.AreEqual(1, cut.FindAll(".verso-marketplace-row").Count);
        // The search result is what knows the description; the installed entry is what knows
        // it is installed. The merged row carries both.
        Assert.IsTrue(cut.Markup.Contains("Dependency graph notebook."));
        Assert.IsTrue(cut.Markup.Contains("verso-marketplace-row--installed"));
    }

    [TestMethod]
    public void Rows_SortByState_FailedThenInstalledThenAvailable()
    {
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Pkg.Working", "1.0.0", false),
            new("Pkg.Broken", "1.0.0", false, "the extension is not installed on this machine")
        };
        _service.SearchResults = new List<PackageSearchResultDto>
        {
            new("Pkg.Available", "2.0.0", null, null, null, null, null, false)
        };

        var cut = RenderSearch("pkg");

        var ids = cut.FindAll(".verso-marketplace-row-id").Select(e => e.TextContent.Trim()).ToList();
        CollectionAssert.AreEqual(new[] { "Pkg.Broken", "Pkg.Working", "Pkg.Available" }, ids);
    }

    [TestMethod]
    public void Query_FiltersInstalledRowsToo()
    {
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Verso.Showcase.Dag", "1.2.0", false),
            new("Verso.Themes.Nord", "2.0.0", false)
        };
        _service.SearchResults = new List<PackageSearchResultDto>();

        var cut = RenderSearch("nord");

        var ids = cut.FindAll(".verso-marketplace-row-id").Select(e => e.TextContent.Trim()).ToList();
        CollectionAssert.AreEqual(new[] { "Verso.Themes.Nord" }, ids);
    }

    // --- What a package adds ---

    [TestMethod]
    public void InstalledPackage_ShowsWhatItRegistered()
    {
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Verso.Showcase.Dag", "1.2.0", false, null, new[] { "LayoutEngine", "CellType" })
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        var chips = cut.FindAll(".verso-marketplace-add").Select(e => e.TextContent.Trim()).ToList();
        CollectionAssert.AreEqual(new[] { "Layout Engine", "Cell Type" }, chips);
    }

    [TestMethod]
    public void InstalledPackage_ThatRegisteredNothing_IsFlagged()
    {
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Newtonsoft.Json", "13.0.4", false, null, Array.Empty<string>())
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        Assert.IsTrue(cut.Markup.Contains(UI.Marketplace_AddsNothing));
    }

    [TestMethod]
    public void InstalledPackage_WithNothingRecorded_IsNotFlagged()
    {
        // A null capability list means the package has not loaded, not that it adds nothing.
        // Claiming the latter would be wrong for anything that failed to load.
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Verso.Showcase.Dag", "1.2.0", false)
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        Assert.IsFalse(cut.Markup.Contains(UI.Marketplace_AddsNothing));
    }

    [TestMethod]
    public void InstalledPackage_WithAnIcon_PaintsItOverTheTile()
    {
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Verso.Showcase.Dag", "1.2.0", false, null, null,
                "data:image/png;base64,iVBORw0KGgo=")
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        var icon = cut.Find(".verso-marketplace-tile .verso-marketplace-tile-icon");
        Assert.AreEqual("data:image/png;base64,iVBORw0KGgo=", icon.GetAttribute("src"));

        // The letter stays underneath so a broken icon degrades to it rather than to a blank.
        StringAssert.Contains(cut.Find(".verso-marketplace-tile").TextContent.Trim(), "D");
    }

    [TestMethod]
    public void InstalledPackage_WithNoIcon_KeepsTheLetteredTile()
    {
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Verso.Showcase.Dag", "1.2.0", false)
        };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        Assert.AreEqual(0, cut.FindAll(".verso-marketplace-tile-icon").Count);
        Assert.AreEqual("D", cut.Find(".verso-marketplace-tile").TextContent.Trim());
    }

    [TestMethod]
    public void SearchResult_UsesTheIconTheFeedReported()
    {
        _service.SearchResults = new List<PackageSearchResultDto>
        {
            new("Newtonsoft.Json", "13.0.4", "JSON framework.", "James Newton-King", 4_900_000_000,
                "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.4/icon", null, false)
        };

        var cut = RenderSearch("Newtonsoft");

        var icon = cut.Find(".verso-marketplace-tile-icon");
        Assert.AreEqual(
            "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.4/icon",
            icon.GetAttribute("src"));
    }

    [TestMethod]
    public void SearchResult_WithNoIcon_KeepsTheLetteredTile()
    {
        _service.SearchResults = new List<PackageSearchResultDto>
        {
            new("Verso.Showcase.Dag", "1.2.0", "A layout.", "Datafication", 100, null, null, false)
        };

        var cut = RenderSearch("Verso.Showcase");

        Assert.AreEqual(0, cut.FindAll(".verso-marketplace-tile-icon").Count);
        Assert.AreEqual("D", cut.Find(".verso-marketplace-tile").TextContent.Trim());
    }

    [TestMethod]
    public void InstalledPackage_PrefersTheLocalCopyOverTheFeedUrl()
    {
        // The copy on disk needs no network, so it wins when both are available.
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Newtonsoft.Json", "13.0.4", false, null, null, "data:image/png;base64,iVBORw0KGgo=")
        };
        _service.SearchResults = new List<PackageSearchResultDto>
        {
            new("Newtonsoft.Json", "13.0.4", "JSON framework.", "James Newton-King", 4_900_000_000,
                "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.4/icon", null, true)
        };

        var cut = RenderSearch("Newtonsoft");

        Assert.AreEqual("data:image/png;base64,iVBORw0KGgo=",
            cut.Find(".verso-marketplace-tile-icon").GetAttribute("src"));
    }

    [TestMethod]
    public void InstalledPackage_WithNoLocalCopy_FallsBackToTheFeedUrl()
    {
        // Covers a package installed before icons were kept alongside it: the row still shows
        // artwork as soon as the package turns up in a search.
        _service.InstalledExtensions = new List<InstalledExtensionDto>
        {
            new("Newtonsoft.Json", "13.0.4", false)
        };
        _service.SearchResults = new List<PackageSearchResultDto>
        {
            new("Newtonsoft.Json", "13.0.4", "JSON framework.", "James Newton-King", 4_900_000_000,
                "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.4/icon", null, true)
        };

        var cut = RenderSearch("Newtonsoft");

        Assert.AreEqual(
            "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.4/icon",
            cut.Find(".verso-marketplace-tile-icon").GetAttribute("src"));
    }

    [TestMethod]
    public void SearchResult_NotInstalled_ClaimsNothingAboutWhatItAdds()
    {
        _service.SearchResults = new List<PackageSearchResultDto>
        {
            new("Newtonsoft.Json", "13.0.4", "JSON framework.", "James Newton-King", 4_900_000_000, null, null, false)
        };

        var cut = RenderSearch("json");

        Assert.IsFalse(cut.Markup.Contains(UI.Marketplace_AddsNothing));
        Assert.AreEqual(0, cut.FindAll(".verso-marketplace-add").Count);
    }

    // --- The two context chips ---

    [TestMethod]
    public void ContextChips_StateScopeAndSource()
    {
        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        var chips = cut.FindAll(".verso-marketplace-context-chip").Select(e => e.TextContent.Trim()).ToList();
        Assert.AreEqual(2, chips.Count);
        Assert.IsTrue(chips[0].Contains(UI.Marketplace_ThisNotebook));
        Assert.IsTrue(chips[1].Contains("nuget.org"));
    }

    [TestMethod]
    public void SourceChip_CountsWhenThereIsMoreThanOne()
    {
        _service.MarketplaceSources = new List<string> { "nuget.org", "acme-internal" };

        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        var chips = cut.FindAll(".verso-marketplace-context-chip");
        Assert.IsTrue(chips[1].TextContent.Contains("2 sources"));
        // The names themselves are the tooltip, so a corporate feed is nameable without
        // spending row width on it.
        Assert.IsTrue(chips[1].GetAttribute("data-verso-tip-desc")!.Contains("acme-internal"));
    }

    [TestMethod]
    public void DownloadCounts_AreShortened()
    {
        _service.SearchResults = new List<PackageSearchResultDto>
        {
            new("Big.Package", "1.0.0", null, null, 4_900_000_000, null, null, false),
            new("Small.Package", "1.0.0", null, null, 15_400, null, null, false)
        };

        var cut = RenderSearch("package");

        var meta = cut.FindAll(".verso-marketplace-row-meta").Select(e => e.TextContent.Trim()).ToList();
        CollectionAssert.AreEqual(new[] { "4.9B downloads", "15.4k downloads" }, meta);
    }

    // Types into the search box and waits out the component's debounce, so the rendered
    // list reflects a real search rather than a field poked directly.
    private IRenderedComponent<ExtensionPanel> RenderSearch(string query)
    {
        var cut = RenderComponent<ExtensionPanel>(p => p
            .Add(e => e.Service, _service));

        cut.Find(".verso-marketplace-input").Input(query);
        cut.WaitForAssertion(
            () => Assert.IsTrue(_service.SearchExtensionCalls.Count > 0),
            TimeSpan.FromSeconds(5));
        cut.WaitForState(() => !cut.Markup.Contains("Searching..."), TimeSpan.FromSeconds(5));
        return cut;
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
