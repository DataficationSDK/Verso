using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Stubs;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class PresentationLayoutTests
{
    private readonly PresentationLayout _layout = new();
    private readonly StubVersoContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.layout.presentation", _layout.ExtensionId);

    [TestMethod]
    public void LayoutId_IsPresentation()
        => Assert.AreEqual("presentation", _layout.LayoutId);

    [TestMethod]
    public void DisplayName_IsPresentation()
        => Assert.AreEqual("Presentation", _layout.DisplayName);

    [TestMethod]
    public void RequiresCustomRenderer_IsTrue()
        => Assert.IsTrue(_layout.RequiresCustomRenderer);

    [TestMethod]
    public void Capabilities_IsNone()
    {
        Assert.AreEqual(LayoutCapabilities.None, _layout.Capabilities);
    }

    [TestMethod]
    public async Task RenderLayoutAsync_EmitsSlotPlaceholderPerVisibleCell()
    {
        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Outputs = { new CellOutput("text/plain", "hello") }
        };

        var result = await _layout.RenderLayoutAsync(new List<CellModel> { cell }, _context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains($"data-cell-slot=\"{cell.Id}\""),
            "Slot wrapper must carry data-cell-slot so the portal can move <Cell> in.");
        Assert.IsTrue(result.Content.Contains("verso-presentation-cell"),
            "Slot wrapper carries the .verso-presentation-cell class for CSS chrome-hiding.");
    }

    [TestMethod]
    public async Task RenderLayoutAsync_DoesNotInlineOutputContent()
    {
        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Outputs = { new CellOutput("text/plain", "<script>alert('xss')</script>") }
        };

        var result = await _layout.RenderLayoutAsync(new List<CellModel> { cell }, _context);

        // Output content is now rendered by the <Cell> Blazor component via portal; the
        // layout HTML must not contain the raw text (avoids double-rendering and the
        // pre-existing XSS-encoding surface migrates with the Cell component).
        Assert.IsFalse(result.Content.Contains("alert"),
            "Layout HTML must not inline cell output content.");
    }

    [TestMethod]
    public async Task RenderLayoutAsync_SkipsCellsWithNoOutputs()
    {
        var cellWithOutput = new CellModel
        {
            Id = Guid.NewGuid(),
            Outputs = { new CellOutput("text/plain", "hello") }
        };
        var cellWithoutOutput = new CellModel { Id = Guid.NewGuid() };

        var result = await _layout.RenderLayoutAsync(
            new List<CellModel> { cellWithOutput, cellWithoutOutput }, _context);

        Assert.IsTrue(result.Content.Contains(cellWithOutput.Id.ToString()));
        Assert.IsFalse(result.Content.Contains(cellWithoutOutput.Id.ToString()),
            "Cells with no outputs have nothing to show in presentation mode.");
    }

    [TestMethod]
    public async Task RenderLayoutAsync_TagsOutputOnlyCells()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "code", defaultVisibility: CellVisibilityHint.OutputOnly);
        var context = new StubVersoContext
        {
            ExtensionHost = new StubExtensionHostContext(
                () => Array.Empty<ILanguageKernel>(),
                () => new ICellRenderer[] { renderer })
        };

        var cell = new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "code",
            Outputs = { new CellOutput("text/plain", "hello") }
        };

        var result = await _layout.RenderLayoutAsync(new List<CellModel> { cell }, context);

        Assert.IsTrue(result.Content.Contains("verso-presentation-cell--output-only"),
            "Output-only cells get a modifier class so CSS can vary chrome.");
    }

    [TestMethod]
    public async Task RenderLayoutAsync_EmptyCells_ProducesEmptyContainer()
    {
        var result = await _layout.RenderLayoutAsync(new List<CellModel>(), _context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("verso-presentation-view"));
        Assert.IsFalse(result.Content.Contains("verso-presentation-cell"));
    }

    [TestMethod]
    public async Task GetCellContainerAsync_ReturnsDefaultDimensions()
    {
        var cellId = Guid.NewGuid();
        var container = await _layout.GetCellContainerAsync(cellId, _context);

        Assert.AreEqual(cellId, container.CellId);
        Assert.AreEqual(800, container.Width);
        Assert.AreEqual(120, container.Height);
    }

    [TestMethod]
    public async Task LifecycleMethods_DoNotThrow()
    {
        var id = Guid.NewGuid();
        await _layout.OnLoadedAsync(null!);
        await _layout.OnUnloadedAsync();
        await _layout.OnCellAddedAsync(id, 0, _context);
        await _layout.OnCellRemovedAsync(id, _context);
        await _layout.OnCellMovedAsync(id, 1, _context);
        await _layout.ApplyLayoutMetadata(new Dictionary<string, object>(), _context);
    }

    [TestMethod]
    public void GetLayoutMetadata_ReturnsEmptyDictionary()
    {
        var metadata = _layout.GetLayoutMetadata();
        Assert.AreEqual(0, metadata.Count);
    }

    [TestMethod]
    public async Task MetadataRoundTrip_IsNoOp()
    {
        var metadata = _layout.GetLayoutMetadata();

        var restored = new PresentationLayout();
        await restored.ApplyLayoutMetadata(metadata, _context);

        var restoredMetadata = restored.GetLayoutMetadata();
        Assert.AreEqual(0, restoredMetadata.Count);
    }
}
