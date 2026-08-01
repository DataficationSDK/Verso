using Bunit;
using Verso.Abstractions;
using Verso.Blazor.Components.Pages;
using Verso.Blazor.Shared.Models;
using Verso.Blazor.Shared.Tests.Fakes;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The page end of comparing: the panel raises intents, the page runs the comparison, and
/// the result reaches both the cells (as marks) and the overlay. Rendered through the real
/// page because the value of the panel is precisely that these three stay in step.
/// </summary>
[TestClass]
public sealed class NotebookPageCompareTests : BunitTestContext
{
    private const string ChangedSource = "var revenue = await warehouse;";

    [TestMethod]
    public void ComparePill_IsOffered()
    {
        var (service, _) = CreateService();
        TestContext!.Services.AddSingleton<Verso.Blazor.Shared.Services.INotebookService>(service);
        TestContext.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = TestContext.RenderComponent<NotebookPage>();

        Assert.IsNotNull(
            cut.FindAll(".verso-toolbar-toggle").FirstOrDefault(b => b.GetAttribute("data-verso-tip") == "Compare"),
            "Compare is always offered: a baseline can be chosen from disk whatever else is true.");
    }

    [TestMethod]
    public void ChoosingABaseline_MarksTheChangedCell()
    {
        var (service, changedCellId) = CreateService();
        TestContext!.Services.AddSingleton<Verso.Blazor.Shared.Services.INotebookService>(service);
        TestContext.JSInterop.Mode = JSRuntimeMode.Loose;
        service.DiffResultToReturn = DiffWith(changedCellId);

        var cut = TestContext.RenderComponent<NotebookPage>();
        OpenComparePanel(cut);

        cut.FindAll(".verso-panel-row").First(r => r.TextContent.Contains("Last Saved")).Click();

        Assert.AreEqual("lastSaved", service.LastDiffSourceId);
        Assert.AreEqual(1, cut.FindAll(".verso-cell--diff-modified").Count,
            "A live comparison marks the cell in place, not only in the panel.");
        StringAssert.Contains(cut.Markup, "1 modified");
    }

    [TestMethod]
    public void OpeningTheFullDiff_LeavesThePanelStanding()
    {
        // The whole reason the overlay covers the editor area rather than the window: the
        // change list that sent you into the diff is still there when you arrive.
        var (service, changedCellId) = CreateService();
        TestContext!.Services.AddSingleton<Verso.Blazor.Shared.Services.INotebookService>(service);
        TestContext.JSInterop.Mode = JSRuntimeMode.Loose;
        service.DiffResultToReturn = DiffWith(changedCellId);

        var cut = TestContext.RenderComponent<NotebookPage>();
        OpenComparePanel(cut);
        cut.FindAll(".verso-panel-row").First(r => r.TextContent.Contains("Last Saved")).Click();

        cut.FindAll(".verso-panel-action").First(b => b.TextContent.Contains("Open full diff")).Click();

        Assert.AreEqual(1, cut.FindAll(".verso-diff-overlay").Count);
        Assert.AreEqual(1, cut.FindAll(".verso-notebook-editor-area .verso-diff-overlay").Count,
            "The overlay lives inside the editor area, so the toolbar and the panel stay visible.");
        Assert.AreEqual(1, cut.FindAll(".verso-panel-content").Count);
    }

    [TestMethod]
    public void Stopping_ClearsTheMarksAndTheOverlay()
    {
        var (service, changedCellId) = CreateService();
        TestContext!.Services.AddSingleton<Verso.Blazor.Shared.Services.INotebookService>(service);
        TestContext.JSInterop.Mode = JSRuntimeMode.Loose;
        service.DiffResultToReturn = DiffWith(changedCellId);

        var cut = TestContext.RenderComponent<NotebookPage>();
        OpenComparePanel(cut);
        cut.FindAll(".verso-panel-row").First(r => r.TextContent.Contains("Last Saved")).Click();
        cut.FindAll(".verso-panel-action").First(b => b.TextContent.Contains("Open full diff")).Click();

        cut.FindAll(".verso-panel-action").First(b => b.TextContent.Trim() == "Stop").Click();

        Assert.AreEqual(0, cut.FindAll(".verso-cell--diff-modified").Count);
        Assert.AreEqual(0, cut.FindAll(".verso-diff-overlay").Count);
        StringAssert.Contains(cut.Markup, "Compare with");
    }

    [TestMethod]
    public void HostRequestedComparison_OpensThePanel()
    {
        // In VS Code this arrives from the "Compare Notebook with..." command. It opens the
        // panel rather than dropping the reader straight into a full-screen diff.
        var (service, changedCellId) = CreateService();
        TestContext!.Services.AddSingleton<Verso.Blazor.Shared.Services.INotebookService>(service);
        TestContext.JSInterop.Mode = JSRuntimeMode.Loose;
        service.DiffResultToReturn = DiffWith(changedCellId);

        var cut = TestContext.RenderComponent<NotebookPage>();
        cut.InvokeAsync(() => service.RaiseDiffRequested("gitHead")).GetAwaiter().GetResult();
        cut.Render();

        Assert.AreEqual("gitHead", service.LastDiffSourceId);
        Assert.AreEqual("COMPARE", cut.Find(".verso-panel-title").TextContent);
        Assert.AreEqual(0, cut.FindAll(".verso-diff-overlay").Count,
            "A comparison from outside lands in the panel, not over the notebook.");
    }

    private static void OpenComparePanel(IRenderedComponent<NotebookPage> cut)
        => cut.FindAll(".verso-toolbar-toggle").First(b => b.GetAttribute("data-verso-tip") == "Compare").Click();

    private static (FakeNotebookService Service, Guid ChangedCellId) CreateService()
    {
        var cell = new CellModel { Type = "code", Language = "csharp", Source = ChangedSource };
        var service = new FakeNotebookService
        {
            IsLoaded = true,
            Cells = new List<CellModel> { cell },
            LayoutCapabilities = LayoutCapabilities.CellEdit,
            Panels = HostPanels.Available(supportsPropertiesPanel: true, hasViewChoices: true).ToList(),
        };

        return (service, cell.Id);
    }

    private static NotebookDiffResult DiffWith(Guid changedCellId) => new()
    {
        BaselineLabel = "Last Saved",
        Summary = new NotebookDiffSummary { Modified = 1 },
        Cells = new List<CellDiffEntry>
        {
            new()
            {
                Kind = CellDiffKind.Modified,
                CurrentIndex = 0,
                BaselineIndex = 0,
                CurrentCell = new CellModel { Id = changedCellId, Source = ChangedSource },
                BaselineCell = new CellModel { Id = changedCellId, Source = "var revenue = await db;" },
            }
        }
    };
}
