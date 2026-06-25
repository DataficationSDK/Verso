using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class NotebookLayoutTests
{
    private readonly NotebookLayout _layout = new();
    private readonly StubVersoContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.layout.notebook", _layout.ExtensionId);

    [TestMethod]
    public void LayoutId_IsNotebook()
        => Assert.AreEqual("notebook", _layout.LayoutId);

    [TestMethod]
    public void Capabilities_HasAllFlags()
    {
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellReorder));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellEdit));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellResize));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.MultiSelect));
    }

    [TestMethod]
    public void RequiresCustomRenderer_IsTrue()
        => Assert.IsTrue(_layout.RequiresCustomRenderer);

    [TestMethod]
    public async Task RenderLayoutAsync_EmitsOneSlotPerCell()
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
        // The layout portals live cells into slots; it emits one data-cell-slot per cell, keyed
        // by cell id, plus the bottom add row.
        Assert.IsTrue(result.Content.Contains("vmd-cells"));
        Assert.IsTrue(result.Content.Contains($"data-cell-slot=\"{cellId1}\""));
        Assert.IsTrue(result.Content.Contains($"data-cell-slot=\"{cellId2}\""));
        Assert.IsTrue(result.Content.Contains("data-action=\"insert-cell\""));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_DoesNotInlineCellSource()
    {
        // The live cell component is portaled into the slot by the host, so the layout never
        // inlines the cell's source text. That also means there is no source-injection surface in
        // the layout HTML.
        var cells = new List<CellModel>
        {
            new() { Source = "<script>alert('xss')</script>" }
        };

        var result = await _layout.RenderLayoutAsync(cells, _context);
        Assert.IsFalse(result.Content.Contains("<script>alert"));
        Assert.IsFalse(result.Content.Contains("alert('xss')"));
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
}
