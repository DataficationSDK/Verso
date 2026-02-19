using Verso.Abstractions;
using Verso.Sample.Slides;
using Verso.Sample.Slides.Models;
using Verso.Testing.Stubs;

namespace Verso.Sample.Slides.Tests;

[TestClass]
public sealed class PresentationLayoutTests
{
    private readonly PresentationLayout _layout = new();
    private readonly StubVersoContext _context = new();

    // --- Metadata ---

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("com.verso.sample.presentation", _layout.ExtensionId);

    [TestMethod]
    public void LayoutId_IsPresentation()
        => Assert.AreEqual("presentation", _layout.LayoutId);

    [TestMethod]
    public void RequiresCustomRenderer_IsTrue()
        => Assert.IsTrue(_layout.RequiresCustomRenderer);

    [TestMethod]
    public void DisplayName_IsPresentation()
        => Assert.AreEqual("Presentation", _layout.DisplayName);

    // --- Capabilities ---

    [TestMethod]
    public void Capabilities_HasExpectedFlags()
    {
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellReorder));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellEdit));
        Assert.IsTrue(_layout.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
    }

    [TestMethod]
    public void Capabilities_DoesNotHaveResizeOrMultiSelect()
    {
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.CellResize));
        Assert.IsFalse(_layout.Capabilities.HasFlag(LayoutCapabilities.MultiSelect));
    }

    // --- Slide Assignment ---

    [TestMethod]
    public async Task OnCellAdded_AssignsToNextSlide()
    {
        var cell1 = Guid.NewGuid();
        var cell2 = Guid.NewGuid();

        await _layout.OnCellAddedAsync(cell1, 0, _context);
        await _layout.OnCellAddedAsync(cell2, 1, _context);

        var a1 = _layout.GetSlideAssignment(cell1);
        var a2 = _layout.GetSlideAssignment(cell2);

        Assert.IsNotNull(a1);
        Assert.IsNotNull(a2);
        Assert.AreEqual(1, a1!.SlideNumber);
        Assert.AreEqual(2, a2!.SlideNumber);
    }

    [TestMethod]
    public async Task OnCellAdded_SetsCurrentSlideToFirstSlide()
    {
        var cell1 = Guid.NewGuid();
        Assert.AreEqual(0, _layout.CurrentSlide);

        await _layout.OnCellAddedAsync(cell1, 0, _context);
        Assert.AreEqual(1, _layout.CurrentSlide);
    }

    [TestMethod]
    public async Task OnCellRemoved_RemovesSlideAssignment()
    {
        var cellId = Guid.NewGuid();
        await _layout.OnCellAddedAsync(cellId, 0, _context);
        await _layout.OnCellRemovedAsync(cellId, _context);

        Assert.IsNull(_layout.GetSlideAssignment(cellId));
    }

    [TestMethod]
    public async Task OnCellRemoved_LeavesGaps()
    {
        var cell1 = Guid.NewGuid();
        var cell2 = Guid.NewGuid();
        var cell3 = Guid.NewGuid();

        await _layout.OnCellAddedAsync(cell1, 0, _context);
        await _layout.OnCellAddedAsync(cell2, 1, _context);
        await _layout.OnCellAddedAsync(cell3, 2, _context);

        // Remove cell on slide 2; slides 1 and 3 remain, no renumbering
        await _layout.OnCellRemovedAsync(cell2, _context);

        Assert.AreEqual(1, _layout.GetSlideAssignment(cell1)!.SlideNumber);
        Assert.AreEqual(3, _layout.GetSlideAssignment(cell3)!.SlideNumber);
    }

    // --- Navigation ---

    [TestMethod]
    public async Task NavigateToSlide_ChangesCurrentSlide()
    {
        var cell1 = Guid.NewGuid();
        var cell2 = Guid.NewGuid();
        await _layout.OnCellAddedAsync(cell1, 0, _context);
        await _layout.OnCellAddedAsync(cell2, 1, _context);

        _layout.NavigateToSlide(2);
        Assert.AreEqual(2, _layout.CurrentSlide);
    }

    [TestMethod]
    public async Task NavigateToSlide_IgnoresOutOfRange()
    {
        var cell1 = Guid.NewGuid();
        await _layout.OnCellAddedAsync(cell1, 0, _context);

        _layout.NavigateToSlide(0);
        Assert.AreEqual(1, _layout.CurrentSlide);

        _layout.NavigateToSlide(99);
        Assert.AreEqual(1, _layout.CurrentSlide);
    }

    // --- Rendering ---

    [TestMethod]
    public async Task RenderLayoutAsync_ProducesSlideHtml()
    {
        var cell1 = Guid.NewGuid();
        var cell2 = Guid.NewGuid();
        var cells = new List<CellModel>
        {
            new() { Id = cell1, Source = "slide one content" },
            new() { Id = cell2, Source = "slide two content" }
        };

        await _layout.OnCellAddedAsync(cell1, 0, _context);
        await _layout.OnCellAddedAsync(cell2, 1, _context);

        var result = await _layout.RenderLayoutAsync(cells, _context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("verso-presentation"));
        Assert.IsTrue(result.Content.Contains("verso-slide-container"));
        Assert.IsTrue(result.Content.Contains("verso-slide-nav"));
        Assert.IsTrue(result.Content.Contains("verso-slide-counter"));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_ShowsOnlyCurrentSlide()
    {
        var cell1 = Guid.NewGuid();
        var cell2 = Guid.NewGuid();
        var cells = new List<CellModel>
        {
            new() { Id = cell1, Source = "slide one" },
            new() { Id = cell2, Source = "slide two" }
        };

        await _layout.OnCellAddedAsync(cell1, 0, _context);
        await _layout.OnCellAddedAsync(cell2, 1, _context);

        // Current slide is 1
        var result = await _layout.RenderLayoutAsync(cells, _context);

        // Cell 1 is on slide 1, should be visible (display:block)
        Assert.IsTrue(result.Content.Contains($"data-cell-id=\"{cell1}\" data-slide=\"1\" style=\"display:block"));
        // Cell 2 is on slide 2, should be hidden (display:none)
        Assert.IsTrue(result.Content.Contains($"data-cell-id=\"{cell2}\" data-slide=\"2\" style=\"display:none"));
    }

    [TestMethod]
    public async Task RenderLayoutAsync_HasNavigationControls()
    {
        var cell1 = Guid.NewGuid();
        await _layout.OnCellAddedAsync(cell1, 0, _context);

        var cells = new List<CellModel> { new() { Id = cell1, Source = "content" } };
        var result = await _layout.RenderLayoutAsync(cells, _context);

        Assert.IsTrue(result.Content.Contains("data-action=\"prev-slide\""));
        Assert.IsTrue(result.Content.Contains("data-action=\"next-slide\""));
        Assert.IsTrue(result.Content.Contains("1 / 1"));
    }

    // --- Cell Container ---

    [TestMethod]
    public async Task GetCellContainer_CurrentSlide_IsVisible()
    {
        var cellId = Guid.NewGuid();
        await _layout.OnCellAddedAsync(cellId, 0, _context);

        var container = await _layout.GetCellContainerAsync(cellId, _context);

        Assert.AreEqual(cellId, container.CellId);
        Assert.AreEqual(1024, container.Width);
        Assert.AreEqual(768, container.Height);
        Assert.IsTrue(container.IsVisible);
    }

    [TestMethod]
    public async Task GetCellContainer_DifferentSlide_IsNotVisible()
    {
        var cell1 = Guid.NewGuid();
        var cell2 = Guid.NewGuid();

        await _layout.OnCellAddedAsync(cell1, 0, _context);
        await _layout.OnCellAddedAsync(cell2, 1, _context);

        // Current slide is 1, cell2 is on slide 2
        var container = await _layout.GetCellContainerAsync(cell2, _context);
        Assert.IsFalse(container.IsVisible);
    }

    // --- AssignCellToSlide ---

    [TestMethod]
    public void AssignCellToSlide_SetsAssignment()
    {
        var cellId = Guid.NewGuid();
        _layout.AssignCellToSlide(cellId, 3, "fade", true);

        var assignment = _layout.GetSlideAssignment(cellId);
        Assert.IsNotNull(assignment);
        Assert.AreEqual(3, assignment!.SlideNumber);
        Assert.AreEqual("fade", assignment.Transition);
        Assert.IsTrue(assignment.IsTitle);
    }

    // --- Metadata ---

    [TestMethod]
    public async Task GetLayoutMetadata_ReturnsSlideAssignments()
    {
        var cellId = Guid.NewGuid();
        await _layout.OnCellAddedAsync(cellId, 0, _context);

        var metadata = _layout.GetLayoutMetadata();
        Assert.IsTrue(metadata.ContainsKey("currentSlide"));
        Assert.IsTrue(metadata.ContainsKey("cells"));

        var cells = (Dictionary<string, object>)metadata["cells"];
        Assert.IsTrue(cells.ContainsKey(cellId.ToString()));

        var slideDict = (Dictionary<string, object>)cells[cellId.ToString()];
        Assert.AreEqual(1, slideDict["slide"]);
        Assert.AreEqual("none", slideDict["transition"]);
        Assert.AreEqual(false, slideDict["isTitle"]);
    }

    [TestMethod]
    public void GetLayoutMetadata_EmptyWhenNoCells()
    {
        var metadata = _layout.GetLayoutMetadata();
        Assert.AreEqual(0, metadata.Count);
    }

    [TestMethod]
    public async Task ApplyLayoutMetadata_RestoresSlideAssignments()
    {
        var cellId = Guid.NewGuid();
        var metadata = new Dictionary<string, object>
        {
            ["currentSlide"] = 2,
            ["cells"] = new Dictionary<string, object>
            {
                [cellId.ToString()] = new Dictionary<string, object>
                {
                    ["slide"] = 2,
                    ["transition"] = "fade",
                    ["isTitle"] = true
                }
            }
        };

        await _layout.ApplyLayoutMetadata(metadata, _context);

        Assert.AreEqual(2, _layout.CurrentSlide);
        var assignment = _layout.GetSlideAssignment(cellId);
        Assert.IsNotNull(assignment);
        Assert.AreEqual(2, assignment!.SlideNumber);
        Assert.AreEqual("fade", assignment.Transition);
        Assert.IsTrue(assignment.IsTitle);
    }

    [TestMethod]
    public async Task MetadataRoundTrip_PreservesState()
    {
        var cell1 = Guid.NewGuid();
        var cell2 = Guid.NewGuid();

        await _layout.OnCellAddedAsync(cell1, 0, _context);
        await _layout.OnCellAddedAsync(cell2, 1, _context);
        _layout.AssignCellToSlide(cell1, 1, "fade", true);
        _layout.NavigateToSlide(2);

        // Serialize
        var metadata = _layout.GetLayoutMetadata();

        // Restore to a new layout
        var restored = new PresentationLayout();
        await restored.ApplyLayoutMetadata(metadata, _context);

        Assert.AreEqual(2, restored.CurrentSlide);

        var a1 = restored.GetSlideAssignment(cell1);
        Assert.IsNotNull(a1);
        Assert.AreEqual(1, a1!.SlideNumber);
        Assert.AreEqual("fade", a1.Transition);
        Assert.IsTrue(a1.IsTitle);

        var a2 = restored.GetSlideAssignment(cell2);
        Assert.IsNotNull(a2);
        Assert.AreEqual(2, a2!.SlideNumber);
    }

    // --- SlideAssignment Record ---

    [TestMethod]
    public void SlideAssignment_DefaultValues()
    {
        var assignment = new SlideAssignment(1);
        Assert.AreEqual(1, assignment.SlideNumber);
        Assert.AreEqual("none", assignment.Transition);
        Assert.IsFalse(assignment.IsTitle);
    }
}
