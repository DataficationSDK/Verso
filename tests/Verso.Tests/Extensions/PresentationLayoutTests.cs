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
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellReorder));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellEdit));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellResize));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.MultiSelect));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_SkipsCellsWithNoOutputs()
    {
        var cellWithOutput = new CellModel
        {
            Id = Guid.NewGuid(),
            Source = "Console.WriteLine(\"hello\")",
            Outputs = { new CellOutput("text/plain", "hello") }
        };
        var cellWithoutOutput = new CellModel
        {
            Id = Guid.NewGuid(),
            Source = "var x = 42;"
        };

        var result = await _layout.RenderLayoutAsync(
            new List<CellModel> { cellWithOutput, cellWithoutOutput }, _context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains(cellWithOutput.Id.ToString()));
        Assert.IsFalse(result.Content.Contains(cellWithoutOutput.Id.ToString()));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_ShowsOutputContent()
    {
        var renderer = new FakeCellRenderer(cellTypeId: "code", defaultVisibility: CellVisibilityHint.OutputOnly);
        var context = new StubVersoContext
        {
            ExtensionHost = new StubExtensionHostContext(
                () => Array.Empty<ILanguageKernel>(),
                () => new ICellRenderer[] { renderer })
        };

        var cells = new List<CellModel>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Type = "code",
                Source = "ignored source",
                Outputs = { new CellOutput("text/plain", "the output text") }
            }
        };

        var result = await _layout.RenderLayoutAsync(cells, context);

        Assert.IsTrue(result.Content.Contains("the output text"));
        Assert.IsFalse(result.Content.Contains("ignored source"));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_RendersHtmlOutputAsRaw()
    {
        var cells = new List<CellModel>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Outputs = { new CellOutput("text/html", "<table><tr><td>data</td></tr></table>") }
            }
        };

        var result = await _layout.RenderLayoutAsync(cells, _context);

        Assert.IsTrue(result.Content.Contains("<table><tr><td>data</td></tr></table>"));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_HtmlEncodesPlainTextOutput()
    {
        var cells = new List<CellModel>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Outputs = { new CellOutput("text/plain", "<script>alert('xss')</script>") }
            }
        };

        var result = await _layout.RenderLayoutAsync(cells, _context);

        Assert.IsFalse(result.Content.Contains("<script>alert"));
        Assert.IsTrue(result.Content.Contains("&lt;script&gt;"));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_RendersErrorOutput()
    {
        var cells = new List<CellModel>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Outputs = { new CellOutput("text/plain", "something went wrong") { IsError = true } }
            }
        };

        var result = await _layout.RenderLayoutAsync(cells, _context);

        Assert.IsTrue(result.Content.Contains("verso-output--error"));
        Assert.IsTrue(result.Content.Contains("something went wrong"));
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
