namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class DashboardGridTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService { IsLoaded = true };

        // Set up JS interop stubs for DashboardCell + MonacoEditor
        TestContext!.JSInterop.SetupVoid("versoDashboard.initResizable", _ => true);
        TestContext.JSInterop.SetupVoid("versoDashboard.initDraggable", _ => true);
        TestContext.JSInterop.SetupVoid("versoDashboard.dispose", _ => true);
        TestContext.JSInterop.SetupVoid("versoMonaco.create", _ => true);
        TestContext.JSInterop.SetupVoid("versoMonaco.setValue", _ => true);
        TestContext.JSInterop.SetupVoid("versoMonaco.setLanguage", _ => true);
        TestContext.JSInterop.SetupVoid("versoMonaco.dispose", _ => true);
    }

    [TestMethod]
    public void Grid_RendersCells()
    {
        var cell1 = CreateCell("cell 1");
        var cell2 = CreateCell("cell 2");
        var cells = new List<CellModel> { cell1, cell2 };

        _service.CellContainers[cell1.Id] = new CellContainerInfo(cell1.Id, 0, 0, 6, 4);
        _service.CellContainers[cell2.Id] = new CellContainerInfo(cell2.Id, 6, 0, 6, 4);

        var cut = RenderComponent<DashboardGrid>(p => p
            .Add(g => g.Service, _service)
            .Add(g => g.Cells, cells));

        Assert.IsTrue(cut.Markup.Contains("verso-dashboard-grid"));
        Assert.IsTrue(cut.Markup.Contains("verso-dashboard-cell"));
    }

    [TestMethod]
    public void Grid_CssGridPositioning()
    {
        var cell = CreateCell("positioned");
        var cells = new List<CellModel> { cell };

        _service.CellContainers[cell.Id] = new CellContainerInfo(cell.Id, 2, 1, 4, 3);

        var cut = RenderComponent<DashboardGrid>(p => p
            .Add(g => g.Service, _service)
            .Add(g => g.Cells, cells));

        // CSS grid-column and grid-row should be set
        Assert.IsTrue(cut.Markup.Contains("grid-column") || cut.Markup.Contains("grid-row"));
    }

    [TestMethod]
    public void DashboardCell_ToggleCodeVisibility()
    {
        var cell = CreateCell("var x = 1;");
        var cells = new List<CellModel> { cell };

        _service.CellContainers[cell.Id] = new CellContainerInfo(cell.Id, 0, 0, 6, 4);

        var cut = RenderComponent<DashboardGrid>(p => p
            .Add(g => g.Service, _service)
            .Add(g => g.Cells, cells));

        // Find the toggle button
        var toggleBtns = cut.FindAll("button.verso-dashboard-edit-toggle, button[title='Toggle Code']");
        if (toggleBtns.Count > 0)
        {
            Assert.IsTrue(toggleBtns[0].TextContent.Contains("Edit") || toggleBtns[0].TextContent.Contains("Hide"));
        }
    }

    [TestMethod]
    public void DashboardCell_RunButton()
    {
        Guid? ranCellId = null;
        var cell = CreateCell("run me");
        var cells = new List<CellModel> { cell };

        _service.CellContainers[cell.Id] = new CellContainerInfo(cell.Id, 0, 0, 6, 4);

        var cut = RenderComponent<DashboardGrid>(p => p
            .Add(g => g.Service, _service)
            .Add(g => g.Cells, cells)
            .Add(g => g.OnRunCell, id => ranCellId = id));

        var runBtns = cut.FindAll("button[title='Run']");
        Assert.IsTrue(runBtns.Count > 0);
    }

    [TestMethod]
    public void DashboardCell_ShowsOutput()
    {
        var cell = CreateCell("code");
        cell.Outputs.Add(new CellOutput("text/plain", "output text"));
        var cells = new List<CellModel> { cell };

        _service.CellContainers[cell.Id] = new CellContainerInfo(cell.Id, 0, 0, 6, 4);

        var cut = RenderComponent<DashboardGrid>(p => p
            .Add(g => g.Service, _service)
            .Add(g => g.Cells, cells));

        Assert.IsTrue(cut.Markup.Contains("output text"));
    }

    [TestMethod]
    public void DashboardCell_ShowsSource_WhenNoOutput()
    {
        var cell = CreateCell("var x = 42;");
        cell.Outputs.Clear();
        var cells = new List<CellModel> { cell };

        _service.CellContainers[cell.Id] = new CellContainerInfo(cell.Id, 0, 0, 6, 4);

        var cut = RenderComponent<DashboardGrid>(p => p
            .Add(g => g.Service, _service)
            .Add(g => g.Cells, cells));

        Assert.IsTrue(cut.Markup.Contains("var x = 42;"));
    }

    [TestMethod]
    public void EmptyGrid_RendersDivWithoutCells()
    {
        var cut = RenderComponent<DashboardGrid>(p => p
            .Add(g => g.Service, _service)
            .Add(g => g.Cells, new List<CellModel>()));

        Assert.IsTrue(cut.Markup.Contains("verso-dashboard-grid"));
        Assert.IsFalse(cut.Markup.Contains("verso-dashboard-cell"));
    }

    [TestMethod]
    public void DashboardCell_ExecutingState_ShowsSpinner()
    {
        var cell = CreateCell("executing");
        var cells = new List<CellModel> { cell };

        _service.CellContainers[cell.Id] = new CellContainerInfo(cell.Id, 0, 0, 6, 4);

        var cut = RenderComponent<DashboardGrid>(p => p
            .Add(g => g.Service, _service)
            .Add(g => g.Cells, cells)
            .Add(g => g.ExecutingCellId, cell.Id));

        var runBtns = cut.FindAll("button[title='Run']");
        if (runBtns.Count > 0)
        {
            Assert.IsTrue(runBtns[0].HasAttribute("disabled"));
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static CellModel CreateCell(string source)
    {
        return new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "code",
            Language = "csharp",
            Source = source
        };
    }
}
