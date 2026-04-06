using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Stubs;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class PresentationLayoutVisibilityTests
{
    private readonly PresentationLayout _layout = new();

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
    public async Task RenderLayoutAsync_SkipsCellMarkedHidden()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "code", defaultVisibility: CellVisibilityHint.Content);
        var context = MakeContext(renderer);

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "code",
            Source = "Console.WriteLine(\"hello\")",
            Outputs = { new CellOutput("text/plain", "hello") },
            Metadata = { ["verso:visibility"] = new Dictionary<string, string> { ["presentation"] = "hidden" } }
        };

        var result = await _layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsFalse(result.Content.Contains(cell.Id.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_ShowsInputForVisibleOverride()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "parameters", defaultVisibility: CellVisibilityHint.Infrastructure);
        var context = MakeContext(renderer);

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "parameters",
            Source = "param1 = 42",
            Outputs = { new CellOutput("text/plain", "param1: 42") },
            Metadata = { ["verso:visibility"] = new Dictionary<string, string> { ["presentation"] = "visible" } }
        };

        var result = await _layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsTrue(result.Content.Contains(cell.Id.ToString()));
        Assert.IsTrue(result.Content.Contains("verso-presentation-input"));
        Assert.IsTrue(result.Content.Contains("param1 = 42"));
        Assert.IsTrue(result.Content.Contains("param1: 42"));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_HidesInfrastructureCellWithNoOverride()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "parameters", defaultVisibility: CellVisibilityHint.Infrastructure);
        var context = MakeContext(renderer);

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "parameters",
            Source = "param1 = 42",
            Outputs = { new CellOutput("text/plain", "param1: 42") }
        };

        var result = await _layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsFalse(result.Content.Contains(cell.Id.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_ShowsOutputOnlyForOutputOnlyCell()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "sql", defaultVisibility: CellVisibilityHint.OutputOnly);
        var context = MakeContext(renderer);

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "sql",
            Source = "SELECT * FROM users",
            Outputs = { new CellOutput("text/html", "<table><tr><td>data</td></tr></table>") }
        };

        var result = await _layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsTrue(result.Content.Contains(cell.Id.ToString()));
        Assert.IsFalse(result.Content.Contains("verso-presentation-input"));
        Assert.IsFalse(result.Content.Contains("SELECT * FROM users"));
        Assert.IsTrue(result.Content.Contains("<table><tr><td>data</td></tr></table>"));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_ZeroOutputs_SkippedEvenIfVisible()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "code", defaultVisibility: CellVisibilityHint.Content);
        var context = MakeContext(renderer);

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "code",
            Source = "var x = 42;",
            Metadata = { ["verso:visibility"] = new Dictionary<string, string> { ["presentation"] = "visible" } }
        };

        var result = await _layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsFalse(result.Content.Contains(cell.Id.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_NoRenderer_FallsBackToVisible()
    {
        var context = MakeContext(); // no renderers registered

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "unknown",
            Source = "some input",
            Outputs = { new CellOutput("text/plain", "some output") }
        };

        var result = await _layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsTrue(result.Content.Contains(cell.Id.ToString()));
        Assert.IsTrue(result.Content.Contains("verso-presentation-input"));
        Assert.IsTrue(result.Content.Contains("some input"));
        Assert.IsTrue(result.Content.Contains("some output"));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_MixedVisibility_RendersCorrectCells()
    {
        var contentRenderer = new FakeCellRenderer(
            extensionId: "test.markdown", cellTypeId: "markdown", defaultVisibility: CellVisibilityHint.Content);
        var infraRenderer = new FakeCellRenderer(
            extensionId: "test.params", cellTypeId: "parameters", defaultVisibility: CellVisibilityHint.Infrastructure);
        var outputOnlyRenderer = new FakeCellRenderer(
            extensionId: "test.sql", cellTypeId: "sql", defaultVisibility: CellVisibilityHint.OutputOnly);
        var context = MakeContext(contentRenderer, infraRenderer, outputOnlyRenderer);

        var visibleCell = new CellModel
        {
            Id = Guid.NewGuid(), Type = "markdown", Source = "# Title",
            Outputs = { new CellOutput("text/html", "<h1>Title</h1>") }
        };
        var hiddenCell = new CellModel
        {
            Id = Guid.NewGuid(), Type = "parameters", Source = "p = 1",
            Outputs = { new CellOutput("text/plain", "p: 1") }
        };
        var outputOnlyCell = new CellModel
        {
            Id = Guid.NewGuid(), Type = "sql", Source = "SELECT 1",
            Outputs = { new CellOutput("text/plain", "1") }
        };

        var cells = new List<CellModel> { visibleCell, hiddenCell, outputOnlyCell };
        var result = await _layout.RenderLayoutAsync(cells, context);

        // Visible cell: input + output shown
        Assert.IsTrue(result.Content.Contains(visibleCell.Id.ToString()));
        Assert.IsTrue(result.Content.Contains("# Title"));

        // Infrastructure cell: hidden
        Assert.IsFalse(result.Content.Contains(hiddenCell.Id.ToString()));

        // OutputOnly cell: output shown, input not shown
        Assert.IsTrue(result.Content.Contains(outputOnlyCell.Id.ToString()));
        Assert.IsFalse(result.Content.Contains("SELECT 1"));
    }
}
