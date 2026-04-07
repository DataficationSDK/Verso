using Verso.Extensions.Layouts;
using Verso.Extensions.Renderers;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class VisibilityDefaultsTests
{
    // --- Renderer defaults ---

    [TestMethod]
    public void MarkdownRenderer_DefaultVisibility_IsContent()
    {
        ICellRenderer renderer = new MarkdownRenderer();
        Assert.AreEqual(CellVisibilityHint.Content, renderer.DefaultVisibility);
    }

    [TestMethod]
    public void HtmlCellRenderer_DefaultVisibility_IsContent()
    {
        ICellRenderer renderer = new HtmlCellRenderer();
        Assert.AreEqual(CellVisibilityHint.Content, renderer.DefaultVisibility);
    }

    [TestMethod]
    public void MermaidCellRenderer_DefaultVisibility_IsContent()
    {
        ICellRenderer renderer = new MermaidCellRenderer();
        Assert.AreEqual(CellVisibilityHint.Content, renderer.DefaultVisibility);
    }

    [TestMethod]
    public void ParametersCellRenderer_DefaultVisibility_IsInfrastructure()
    {
        var renderer = new ParametersCellRenderer();
        Assert.AreEqual(CellVisibilityHint.Infrastructure, renderer.DefaultVisibility);
    }

    // --- Layout SupportedVisibilityStates ---

    [TestMethod]
    public void NotebookLayout_SupportedVisibilityStates_ContainsOnlyVisible()
    {
        ILayoutEngine layout = new NotebookLayout();
        var states = layout.SupportedVisibilityStates;

        Assert.AreEqual(1, states.Count);
        Assert.IsTrue(states.Contains(CellVisibilityState.Visible));
    }

    [TestMethod]
    public void PresentationLayout_SupportedVisibilityStates_ContainsVisibleHiddenOutputOnly()
    {
        var layout = new PresentationLayout();
        var states = layout.SupportedVisibilityStates;

        Assert.AreEqual(3, states.Count);
        Assert.IsTrue(states.Contains(CellVisibilityState.Visible));
        Assert.IsTrue(states.Contains(CellVisibilityState.Hidden));
        Assert.IsTrue(states.Contains(CellVisibilityState.OutputOnly));
    }

    [TestMethod]
    public void DashboardLayout_SupportedVisibilityStates_ContainsVisibleHiddenOutputOnly()
    {
        var layout = new DashboardLayout();
        var states = layout.SupportedVisibilityStates;

        Assert.AreEqual(3, states.Count);
        Assert.IsTrue(states.Contains(CellVisibilityState.Visible));
        Assert.IsTrue(states.Contains(CellVisibilityState.Hidden));
        Assert.IsTrue(states.Contains(CellVisibilityState.OutputOnly));
    }

    // --- Layout SupportsPropertiesPanel ---

    [TestMethod]
    public void NotebookLayout_SupportsPropertiesPanel_IsTrue()
    {
        var layout = new NotebookLayout();
        Assert.IsTrue(layout.SupportsPropertiesPanel);
    }

    [TestMethod]
    public void PresentationLayout_SupportsPropertiesPanel_IsFalse()
    {
        ILayoutEngine layout = new PresentationLayout();
        Assert.IsFalse(layout.SupportsPropertiesPanel);
    }

    [TestMethod]
    public void DashboardLayout_SupportsPropertiesPanel_EditMode_IsTrue()
    {
        var layout = new DashboardLayout { IsEditMode = true };
        Assert.IsTrue(layout.SupportsPropertiesPanel);
    }

    [TestMethod]
    public void DashboardLayout_SupportsPropertiesPanel_ViewMode_IsFalse()
    {
        var layout = new DashboardLayout { IsEditMode = false };
        Assert.IsFalse(layout.SupportsPropertiesPanel);
    }
}
