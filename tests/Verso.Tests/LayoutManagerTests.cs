using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Tests.Helpers;

namespace Verso.Tests;

[TestClass]
public sealed class LayoutManagerTests
{
    [TestMethod]
    public void SetActiveLayout_SetsLayoutById()
    {
        var notebook = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook });

        manager.SetActiveLayout("notebook");
        Assert.AreSame(notebook, manager.ActiveLayout);
    }

    [TestMethod]
    public void SetActiveLayout_UnknownId_Throws()
    {
        var manager = new LayoutManager(new ILayoutEngine[] { new NotebookLayout() });
        Assert.ThrowsException<InvalidOperationException>(() => manager.SetActiveLayout("nonexistent"));
    }

    [TestMethod]
    public void Constructor_WithDefaultLayoutId_SetsActiveLayout()
    {
        var notebook = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook }, "notebook");

        Assert.AreSame(notebook, manager.ActiveLayout);
    }

    [TestMethod]
    public void Capabilities_DelegatesToActiveLayout()
    {
        var notebook = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook }, "notebook");

        Assert.AreEqual(notebook.Capabilities, manager.Capabilities);
    }

    [TestMethod]
    public void Capabilities_NoneWhenNoActiveLayout()
    {
        var manager = new LayoutManager(new ILayoutEngine[] { new NotebookLayout() });
        Assert.AreEqual(LayoutCapabilities.None, manager.Capabilities);
    }

    [TestMethod]
    public async Task SaveMetadata_WritesLayoutMetadataToNotebook()
    {
        var notebook = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook }, "notebook");

        var model = new NotebookModel();
        await manager.SaveMetadataAsync(model);

        // NotebookLayout returns empty metadata, so nothing written
        Assert.AreEqual(0, model.Layouts.Count);
    }

    [TestMethod]
    public async Task RestoreMetadata_CallsApplyOnMatchingLayout()
    {
        var notebook = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook });

        var model = new NotebookModel();
        model.Layouts["notebook"] = new Dictionary<string, object> { ["key"] = "value" };

        var context = new StubVersoContext();
        await manager.RestoreMetadataAsync(model, context);

        // NotebookLayout.ApplyLayoutMetadata is a no-op, so just verify no exception
    }

    [TestMethod]
    public void SwitchLayout_ChangesCapabilities()
    {
        var notebook = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook });

        Assert.AreEqual(LayoutCapabilities.None, manager.Capabilities);

        manager.SetActiveLayout("notebook");

        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
    }

    [TestMethod]
    public void AvailableLayouts_ReturnsAll()
    {
        var layouts = new ILayoutEngine[] { new NotebookLayout() };
        var manager = new LayoutManager(layouts);
        Assert.AreEqual(1, manager.AvailableLayouts.Count);
    }
}
