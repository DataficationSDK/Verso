using AngleSharp.Dom;
using Verso.Blazor.Shared.Resources;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// Debugging is a mode that stays on after its panel is closed, so the panel's toggle is the one
/// place that still says the mode is on, and that a cell is stopped and waiting.
/// </summary>
[TestClass]
public sealed class ToolbarDebugToggleTests : BunitTestContext
{
    private FakeNotebookService _service = default!;
    private NotebookPanelInfo _debug = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService { IsLoaded = true, IsEmbedded = false };
        _debug = HostPanels.Find(HostPanels.Debug)!;
        _service.Panels.Add(HostPanels.Find(HostPanels.Compare)!);
        _service.Panels.Add(_debug);
    }

    private IElement RenderToggle(string panelName)
    {
        var cut = RenderComponent<Toolbar>(p => p
            .Add(t => t.Service, _service)
            .Add(t => t.Panels, _service.Panels)
            .Add(t => t.OnTogglePanel, (string _) => { }));

        return cut.Find($".verso-toolbar-toggle[aria-label='{panelName}']");
    }

    private static DebugSnapshot Enabled(DebugStop? stop = null)
        => new(true, new Dictionary<Guid, IReadOnlyList<int>>(), stop);

    [TestMethod]
    public void DebuggingOff_ToggleLooksLikeAnyOther()
    {
        var toggle = RenderToggle(_debug.DisplayName);

        Assert.IsNull(toggle.QuerySelector(".verso-toolbar-toggle-dot"));
        Assert.IsFalse(toggle.ClassList.Contains("verso-toolbar-toggle--on"));
        Assert.IsFalse(toggle.ClassList.Contains("verso-toolbar-toggle--waiting"));
        Assert.AreEqual(_debug.Description, toggle.GetAttribute("data-verso-tip-desc"));
    }

    [TestMethod]
    public void DebuggingOn_ToggleShowsTheDotAndSaysSo()
    {
        _service.DebugState = Enabled();

        var toggle = RenderToggle(_debug.DisplayName);

        Assert.IsTrue(toggle.ClassList.Contains("verso-toolbar-toggle--on"));
        Assert.IsNotNull(toggle.QuerySelector(".verso-toolbar-toggle-dot"));
        Assert.AreEqual(UI.Debug_On, toggle.GetAttribute("data-verso-tip-desc"));
        Assert.IsFalse(toggle.ClassList.Contains("verso-toolbar-toggle--waiting"),
            "Nothing is stopped, so nothing is waiting on the reader.");
    }

    [TestMethod]
    public void CellStopped_ToggleSaysItIsWaiting()
    {
        var cellId = Guid.NewGuid();
        _service.DebugState = Enabled(new DebugStop(DebugStopReason.Breakpoint, new[]
        {
            new DebugFrame("", cellId, 2, Array.Empty<DebugVariable>()),
        }));

        var toggle = RenderToggle(_debug.DisplayName);

        Assert.IsTrue(toggle.ClassList.Contains("verso-toolbar-toggle--waiting"));
        Assert.IsTrue(toggle.ClassList.Contains("verso-toolbar-toggle--on"));
    }

    [TestMethod]
    public void DebuggingOn_OtherTogglesAreUnchanged()
    {
        _service.DebugState = Enabled();
        var compare = HostPanels.Find(HostPanels.Compare)!;

        var toggle = RenderToggle(compare.DisplayName);

        Assert.IsFalse(toggle.ClassList.Contains("verso-toolbar-toggle--on"));
        Assert.IsNull(toggle.QuerySelector(".verso-toolbar-toggle-dot"));
        Assert.AreEqual(compare.Description, toggle.GetAttribute("data-verso-tip-desc"));
    }

    [TestMethod]
    public void ExtensionPanelNamedDebug_IsNotMarked()
    {
        // Only the host's own panel stands for the mode. An extension that happens to choose the
        // same id is a different panel and says nothing about debugging.
        _service.DebugState = Enabled();
        _service.Panels.Clear();
        _service.Panels.Add(new NotebookPanelInfo(
            HostPanels.Debug, "com.example.other", "Other debug", null, null, 600, IsHostPanel: false));

        var toggle = RenderToggle("Other debug");

        Assert.IsFalse(toggle.ClassList.Contains("verso-toolbar-toggle--on"));
        Assert.IsNull(toggle.QuerySelector(".verso-toolbar-toggle-dot"));
    }
}
