using Bunit;
using Microsoft.AspNetCore.Components;
using Verso.Abstractions;
using Verso.Blazor.Shared.Components.Notebook;
using Verso.Blazor.Shared.Models;
using Verso.Blazor.Shared.Tests.Fakes;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The panel is presentation over the comparison state: it renders what is there and raises
/// intents for the page to perform. These tests hold that line, because the moment the panel
/// starts mutating state directly, a comparison stops surviving the panel being closed.
/// </summary>
[TestClass]
public sealed class ComparePanelTests : BunitTestContext
{
    private FakeNotebookService _service = default!;
    private NotebookComparison _comparison = default!;

    [TestInitialize]
    public void SetUp()
    {
        _service = new FakeNotebookService();
        _comparison = new NotebookComparison(_service);
        TestContext!.Services.AddSingleton<Verso.Blazor.Shared.Services.INotebookService>(_service);
        TestContext.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<ComparePanel> Render(
        EventCallback onRefreshSources = default,
        EventCallback<DiffSourceInfo> onCompareRequested = default,
        EventCallback<Guid> onNavigate = default,
        EventCallback onOpenFullDiff = default,
        EventCallback onClear = default)
        => TestContext!.RenderComponent<ComparePanel>(parameters => parameters
            .Add(p => p.Comparison, _comparison)
            .Add(p => p.OnRefreshSources, onRefreshSources)
            .Add(p => p.OnCompareRequested, onCompareRequested)
            .Add(p => p.OnNavigate, onNavigate)
            .Add(p => p.OnOpenFullDiff, onOpenFullDiff)
            .Add(p => p.OnClear, onClear));

    [TestMethod]
    public void WithNoSourcesYet_AsksForThem()
    {
        var asked = false;

        Render(onRefreshSources: EventCallback.Factory.Create(this, () => asked = true));

        Assert.IsTrue(asked, "Opening the panel with no sources loaded has to trigger the load.");
    }

    [TestMethod]
    public async Task SourceList_ShowsEveryBaseline()
    {
        await _comparison.LoadSourcesAsync();

        var cut = Render();

        StringAssert.Contains(cut.Markup, "Last Saved");
        StringAssert.Contains(cut.Markup, "Git: HEAD");
        StringAssert.Contains(cut.Markup, "Choose File...");
    }

    [TestMethod]
    public async Task UnavailableSource_IsShownButNotClickable()
    {
        // Shown rather than hidden, because the reason a git baseline is missing is worth
        // telling: the file is not in a repository.
        _service.DiffSourcesToReturn = new List<DiffSourceInfo>
        {
            new("gitHead", "Git: HEAD", "git", false, "The notebook file is not inside a git repository."),
        };
        await _comparison.LoadSourcesAsync();

        DiffSourceInfo? requested = null;
        var cut = Render(onCompareRequested:
            EventCallback.Factory.Create<DiffSourceInfo>(this, s => requested = s));

        var row = cut.Find(".verso-panel-row");
        StringAssert.Contains(row.ClassName, "verso-panel-row--disabled");
        StringAssert.Contains(row.TextContent, "not inside a git repository");

        row.Click();
        Assert.IsNull(requested, "An unavailable baseline must not start a comparison.");
    }

    [TestMethod]
    public async Task ChoosingASource_RaisesCompareRequested()
    {
        await _comparison.LoadSourcesAsync();
        DiffSourceInfo? requested = null;
        var cut = Render(onCompareRequested:
            EventCallback.Factory.Create<DiffSourceInfo>(this, s => requested = s));

        cut.FindAll(".verso-panel-row").First(r => r.TextContent.Contains("Git: HEAD")).Click();

        Assert.IsNotNull(requested);
        Assert.AreEqual("gitHead", requested.Id);
        Assert.IsNull(_comparison.Result,
            "The panel raises an intent; running the comparison is the page's job.");
    }

    [TestMethod]
    public async Task WithAResult_ShowsSummaryAndChanges()
    {
        await SetUpResultAsync();

        var cut = Render();

        StringAssert.Contains(cut.Markup, "Git: HEAD");
        StringAssert.Contains(cut.Markup, "1 added");
        StringAssert.Contains(cut.Markup, "1 modified");
        Assert.AreEqual(3, cut.FindAll(".verso-compare-kind").Count,
            "One row per change: modified, removed, added.");
    }

    [TestMethod]
    public async Task ClickingAChange_NavigatesToItsCell()
    {
        var ids = await SetUpResultAsync();
        Guid? navigated = null;
        var cut = Render(onNavigate: EventCallback.Factory.Create<Guid>(this, id => navigated = id));

        cut.FindAll(".verso-panel-row--interactive")
            .First(r => r.TextContent.Contains("Added"))
            .Click();

        Assert.AreEqual(ids.Added, navigated);
    }

    [TestMethod]
    public async Task ClickingARemovedChange_ExpandsInsteadOfNavigating()
    {
        // There is no cell left in the notebook to scroll to, so the row shows the baseline
        // source in place rather than pretending to navigate.
        await SetUpResultAsync();
        Guid? navigated = null;
        var cut = Render(onNavigate: EventCallback.Factory.Create<Guid>(this, id => navigated = id));

        cut.FindAll(".verso-panel-row--interactive")
            .First(r => r.TextContent.Contains("Removed"))
            .Click();

        Assert.IsNull(navigated);
        StringAssert.Contains(cut.Find(".verso-compare-removed-source").TextContent, "scratch");
    }

    [TestMethod]
    public async Task OpenFullDiffAndStop_RaiseTheirIntents()
    {
        await SetUpResultAsync();
        var opened = false;
        var cleared = false;
        var cut = Render(
            onOpenFullDiff: EventCallback.Factory.Create(this, () => opened = true),
            onClear: EventCallback.Factory.Create(this, () => cleared = true));

        cut.FindAll(".verso-panel-action").First(b => b.TextContent.Contains("Open full diff")).Click();
        Assert.IsTrue(opened);

        cut.FindAll(".verso-panel-action").First(b => b.TextContent.Trim() == "Stop").Click();
        Assert.IsTrue(cleared);
    }

    [TestMethod]
    public async Task ChangeBaseline_ReturnsToTheSourceListAndRefreshesIt()
    {
        // The set of baselines moves while you work: saving makes "Last Saved" mean something
        // else, and a commit moves HEAD.
        await SetUpResultAsync();
        var refreshCount = 0;
        var cut = Render(onRefreshSources: EventCallback.Factory.Create(this, () => refreshCount++));

        cut.FindAll(".verso-panel-action").First(b => b.TextContent.Contains("Change baseline")).Click();

        StringAssert.Contains(cut.Markup, "Compare with");
        Assert.AreEqual(2, refreshCount, "Once on open, once on asking to change the baseline.");
    }

    [TestMethod]
    public async Task NoCellChanged_SaysSoRatherThanShowingAnEmptyList()
    {
        _service.DiffResultToReturn = new NotebookDiffResult
        {
            BaselineLabel = "Last Saved",
            Cells = new List<CellDiffEntry>(),
            Summary = new NotebookDiffSummary { Unchanged = 4 },
        };
        await _comparison.CompareAsync("lastSaved");

        var cut = Render();

        StringAssert.Contains(cut.Markup, "No cell changed");
    }

    private async Task<(Guid Added, Guid Modified)> SetUpResultAsync()
    {
        var modified = Guid.NewGuid();
        var added = Guid.NewGuid();

        _service.DiffResultToReturn = new NotebookDiffResult
        {
            BaselineLabel = "Git: HEAD",
            Summary = new NotebookDiffSummary { Added = 1, Removed = 1, Modified = 1, Unchanged = 3 },
            Cells = new List<CellDiffEntry>
            {
                new()
                {
                    Kind = CellDiffKind.Modified,
                    CurrentIndex = 0,
                    BaselineIndex = 0,
                    CurrentCell = new CellModel { Id = modified, Source = "var revenue = await warehouse" },
                    BaselineCell = new CellModel { Id = modified, Source = "var revenue = await db" },
                },
                new()
                {
                    Kind = CellDiffKind.Removed,
                    BaselineIndex = 1,
                    BaselineCell = new CellModel { Id = Guid.NewGuid(), Source = "var scratch = revenue.Take(20);" },
                },
                new()
                {
                    Kind = CellDiffKind.Added,
                    CurrentIndex = 1,
                    CurrentCell = new CellModel { Id = added, Source = "var slipping = revenue.Where(...);" },
                },
            }
        };

        await _comparison.CompareAsync("gitHead");
        return (added, modified);
    }
}
