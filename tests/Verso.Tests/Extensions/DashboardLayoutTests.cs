using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class DashboardLayoutTests
{
    private readonly DashboardLayout _layout = new();
    private readonly StubVersoContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.layout.dashboard", _layout.ExtensionId);

    [TestMethod]
    public void LayoutId_IsDashboard()
        => Assert.AreEqual("dashboard", _layout.LayoutId);

    [TestMethod]
    public void RequiresCustomRenderer_IsTrue()
        => Assert.IsTrue(_layout.RequiresCustomRenderer);

    [TestMethod]
    public void Capabilities_ViewMode_HasResizeAndExecute()
    {
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellResize));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellEdit));
    }

    [TestMethod]
    public void Capabilities_ViewMode_NoCellInsert()
    {
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellReorder));
    }

    [TestMethod]
    public void Capabilities_EditMode_HasInsertDeleteReorder()
    {
        _layout.IsEditMode = true;

        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellReorder));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellResize));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellEdit));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_ProducesGridHtml()
    {
        var cellId1 = Guid.NewGuid();
        var cellId2 = Guid.NewGuid();
        var cells = new List<CellModel>
        {
            new() { Id = cellId1, Source = "cell one" },
            new() { Id = cellId2, Source = "cell two" }
        };

        var result = await _layout.RenderLayoutAsync(cells, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("verso-dashboard-grid"));
        Assert.IsTrue(result.Content.Contains("verso-dashboard-cell"));
        Assert.IsTrue(result.Content.Contains("grid-template-columns"));
        Assert.IsTrue(result.Content.Contains(cellId1.ToString()));
        Assert.IsTrue(result.Content.Contains(cellId2.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_HidesCodeShowsOutput()
    {
        var cellId = Guid.NewGuid();
        var cells = new List<CellModel>
        {
            new()
            {
                Id = cellId,
                Source = "Console.WriteLine(\"hello\")",
                Outputs = { new CellOutput("text/plain", "hello") }
            }
        };

        var result = await _layout.RenderLayoutAsync(cells, _context);

        // Output should be present
        Assert.IsTrue(result.Content.Contains("hello"));
        // Source code should not appear (output takes precedence)
        Assert.IsFalse(result.Content.Contains("Console.WriteLine"));
    }

    [TestMethod]
    public async Task OnCellAdded_AssignsDefaultGridPosition()
    {
        var cellId = Guid.NewGuid();
        await _layout.OnCellAddedAsync(cellId, 0, _context);

        var container = await _layout.GetCellContainerAsync(cellId, _context);
        Assert.AreEqual(cellId, container.CellId);
        Assert.AreEqual(6, container.Width);  // Default width
        Assert.AreEqual(4, container.Height); // Default height
        Assert.IsTrue(container.IsVisible);
    }

    [TestMethod]
    public async Task OnCellRemoved_CleansUpPosition()
    {
        var cellId = Guid.NewGuid();
        await _layout.OnCellAddedAsync(cellId, 0, _context);
        await _layout.OnCellRemovedAsync(cellId, _context);

        // Metadata should have no cells
        var metadata = _layout.GetLayoutMetadata();
        if (metadata.TryGetValue("cells", out var cellsObj) && cellsObj is Dictionary<string, object> cells)
        {
            Assert.IsFalse(cells.ContainsKey(cellId.ToString()));
        }
    }

    [TestMethod]
    public async Task GetLayoutMetadata_ReturnsGridPositions()
    {
        var cellId = Guid.NewGuid();
        await _layout.OnCellAddedAsync(cellId, 0, _context);

        var metadata = _layout.GetLayoutMetadata();
        Assert.IsTrue(metadata.ContainsKey("cells"));

        var cells = (Dictionary<string, object>)metadata["cells"];
        Assert.IsTrue(cells.ContainsKey(cellId.ToString()));

        var posDict = (Dictionary<string, object>)cells[cellId.ToString()];
        Assert.AreEqual(0, posDict["row"]);
        Assert.AreEqual(0, posDict["col"]);
        Assert.AreEqual(6, posDict["width"]);
        Assert.AreEqual(4, posDict["height"]);
        Assert.AreEqual(true, posDict["visible"]);
    }

    [TestMethod]
    public async Task ApplyLayoutMetadata_RestoresPositions()
    {
        var cellId = Guid.NewGuid();
        var metadata = new Dictionary<string, object>
        {
            ["cells"] = new Dictionary<string, object>
            {
                [cellId.ToString()] = new Dictionary<string, object>
                {
                    ["row"] = 2,
                    ["col"] = 3,
                    ["width"] = 4,
                    ["height"] = 5,
                    ["visible"] = true
                }
            }
        };

        await _layout.ApplyLayoutMetadata(metadata, _context);

        var container = await _layout.GetCellContainerAsync(cellId, _context);
        Assert.AreEqual(3.0, container.X);    // Column
        Assert.AreEqual(2.0, container.Y);    // Row
        Assert.AreEqual(4.0, container.Width);
        Assert.AreEqual(5.0, container.Height);
        Assert.IsTrue(container.IsVisible);
    }

    [TestMethod]
    public async Task MetadataRoundTrip_PreservesGridState()
    {
        var cellId1 = Guid.NewGuid();
        var cellId2 = Guid.NewGuid();

        await _layout.OnCellAddedAsync(cellId1, 0, _context);
        await _layout.OnCellAddedAsync(cellId2, 1, _context);

        _layout.UpdateCellPosition(cellId1, 1, 2, 8, 3);

        // Serialize
        var metadata = _layout.GetLayoutMetadata();

        // Create a new layout and restore
        var restored = new DashboardLayout();
        await restored.ApplyLayoutMetadata(metadata, _context);

        // Verify cell1 position was restored
        var c1 = await restored.GetCellContainerAsync(cellId1, _context);
        Assert.AreEqual(2.0, c1.X);    // Column
        Assert.AreEqual(1.0, c1.Y);    // Row
        Assert.AreEqual(8.0, c1.Width);
        Assert.AreEqual(3.0, c1.Height);

        // Verify cell2 position was also restored
        var c2 = await restored.GetCellContainerAsync(cellId2, _context);
        Assert.IsTrue(c2.IsVisible);
    }

    [TestMethod]
    public void UpdateCellPosition_ChangesGridPlacement()
    {
        var cellId = Guid.NewGuid();
        _layout.UpdateCellPosition(cellId, 3, 6, 4, 2);

        var container = _layout.GetCellContainerAsync(cellId, _context).GetAwaiter().GetResult();
        Assert.AreEqual(6.0, container.X);    // Column
        Assert.AreEqual(3.0, container.Y);    // Row
        Assert.AreEqual(4.0, container.Width);
        Assert.AreEqual(2.0, container.Height);
    }

    [TestMethod]
    public void DisplayName_IsDashboard()
        => Assert.AreEqual("Dashboard", _layout.DisplayName);

    [TestMethod]
    public void GetLayoutMetadata_EmptyWhenNoCells()
    {
        var metadata = _layout.GetLayoutMetadata();
        Assert.AreEqual(0, metadata.Count);
    }
}
