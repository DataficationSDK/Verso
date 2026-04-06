using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Stubs;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class DashboardLayoutVisibilityTests
{
    private static StubVersoContext MakeContext(params ICellRenderer[] renderers)
    {
        return new StubVersoContext
        {
            ExtensionHost = new StubExtensionHostContext(
                () => Array.Empty<ILanguageKernel>(),
                () => renderers)
        };
    }

    [TestMethod]
    public async Task RenderLayoutAsync_GridOverrideTakesPrecedence_VisibleFalse()
    {
        var layout = new DashboardLayout();
        var context = MakeContext();

        var cellId = Guid.NewGuid();
        // Load metadata with Visible = false
        await layout.ApplyLayoutMetadata(new Dictionary<string, object>
        {
            ["cells"] = new Dictionary<string, object>
            {
                [cellId.ToString()] = new Dictionary<string, object>
                {
                    ["row"] = 0, ["col"] = 0, ["width"] = 6, ["height"] = 4, ["visible"] = false
                }
            }
        }, context);

        var cell = new CellModel
        {
            Id = cellId,
            Type = "code",
            Source = "should not appear"
        };

        var result = await layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsFalse(result.Content.Contains(cellId.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_GridOverrideTakesPrecedence_VisibleTrue()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "code", defaultVisibility: CellVisibilityHint.Infrastructure);
        var layout = new DashboardLayout();
        var context = MakeContext(renderer);

        var cellId = Guid.NewGuid();
        // Load metadata with Visible = true, even though metadata says hidden
        await layout.ApplyLayoutMetadata(new Dictionary<string, object>
        {
            ["cells"] = new Dictionary<string, object>
            {
                [cellId.ToString()] = new Dictionary<string, object>
                {
                    ["row"] = 0, ["col"] = 0, ["width"] = 6, ["height"] = 4, ["visible"] = true
                }
            }
        }, context);

        var cell = new CellModel
        {
            Id = cellId,
            Type = "code",
            Source = "should appear",
            Metadata = { ["verso:visibility"] = new Dictionary<string, string> { ["dashboard"] = "hidden" } }
        };

        var result = await layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsTrue(result.Content.Contains(cellId.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_FallsBackToResolver_InfrastructureHidden()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "parameters", defaultVisibility: CellVisibilityHint.Infrastructure);
        var layout = new DashboardLayout();
        var context = MakeContext(renderer);

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "parameters",
            Source = "param1 = 42"
        };

        var result = await layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsFalse(result.Content.Contains(cell.Id.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_FallsBackToResolver_ContentVisible()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "markdown", defaultVisibility: CellVisibilityHint.Content);
        var layout = new DashboardLayout();
        var context = MakeContext(renderer);

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "markdown",
            Source = "# Dashboard Title"
        };

        var result = await layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsTrue(result.Content.Contains(cell.Id.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_FallsBackToResolver_MetadataOverride()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "parameters", defaultVisibility: CellVisibilityHint.Infrastructure);
        var layout = new DashboardLayout();
        var context = MakeContext(renderer);

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "parameters",
            Source = "param1 = 42",
            Metadata = { ["verso:visibility"] = new Dictionary<string, string> { ["dashboard"] = "visible" } }
        };

        var result = await layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsTrue(result.Content.Contains(cell.Id.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_NoRenderer_NewCell_DefaultsToVisible()
    {
        var layout = new DashboardLayout();
        var context = MakeContext(); // no renderers

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "unknown",
            Source = "some content"
        };

        var result = await layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsTrue(result.Content.Contains(cell.Id.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_ResolverStatePersisted_InGridPosition()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "parameters", defaultVisibility: CellVisibilityHint.Infrastructure);
        var layout = new DashboardLayout();
        var context = MakeContext(renderer);

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "parameters",
            Source = "param1 = 42"
        };

        // First render: resolver determines Hidden, persists into grid position
        await layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        // Second render: grid position now exists with Visible=false, grid takes precedence
        var result = await layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsFalse(result.Content.Contains(cell.Id.ToString()));

        // Verify the grid metadata reflects the resolver decision
        var metadata = layout.GetLayoutMetadata();
        var cells = (Dictionary<string, object>)metadata["cells"];
        var posDict = (Dictionary<string, object>)cells[cell.Id.ToString()];
        Assert.AreEqual(false, posDict["visible"]);
    }
}
