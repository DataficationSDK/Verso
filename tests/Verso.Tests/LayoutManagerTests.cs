using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

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
    public void Capabilities_AllWhenNoActiveLayout()
    {
        var manager = new LayoutManager(new ILayoutEngine[] { new NotebookLayout() });
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellReorder));
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
    public void SwitchLayout_UsesLayoutCapabilities()
    {
        var notebook = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook });

        // Before setting active layout, all capabilities are granted
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellExecute));

        manager.SetActiveLayout("notebook");

        // After setting active layout, capabilities come from the layout
        Assert.AreEqual(notebook.Capabilities, manager.Capabilities);
    }

    [TestMethod]
    public void AvailableLayouts_ReturnsAll()
    {
        var layouts = new ILayoutEngine[] { new NotebookLayout() };
        var manager = new LayoutManager(layouts);
        Assert.AreEqual(1, manager.AvailableLayouts.Count);
    }

    [TestMethod]
    public void SetActiveLayout_Dashboard_UpdatesCapabilities()
    {
        var notebook = new NotebookLayout();
        var dashboard = new DashboardLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook, dashboard });

        manager.SetActiveLayout("dashboard");

        // Dashboard in view mode should not have CellInsert
        Assert.IsFalse(manager.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellResize));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
    }

    [TestMethod]
    public void LayoutChanged_Event_Fires()
    {
        var notebook = new NotebookLayout();
        var dashboard = new DashboardLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook, dashboard });

        ILayoutEngine? firedLayout = null;
        manager.OnLayoutChanged += layout => firedLayout = layout;

        manager.SetActiveLayout("dashboard");

        Assert.IsNotNull(firedLayout);
        Assert.AreSame(dashboard, firedLayout);
    }

    [TestMethod]
    public void RequiresCustomRenderer_DelegatesToActiveLayout()
    {
        var notebook = new NotebookLayout();
        var dashboard = new DashboardLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook, dashboard });

        // No active layout
        Assert.IsFalse(manager.RequiresCustomRenderer);

        // Notebook layout
        manager.SetActiveLayout("notebook");
        Assert.IsFalse(manager.RequiresCustomRenderer);

        // Dashboard layout
        manager.SetActiveLayout("dashboard");
        Assert.IsTrue(manager.RequiresCustomRenderer);
    }

    [TestMethod]
    public void SwitchLayout_CapabilitiesChange_AffectsToolbarActions()
    {
        var notebook = new NotebookLayout();
        var dashboard = new DashboardLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook, dashboard });

        // Start with notebook — all capabilities
        manager.SetActiveLayout("notebook");
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellReorder));

        // Switch to dashboard — restricted capabilities
        manager.SetActiveLayout("dashboard");
        Assert.IsFalse(manager.Capabilities.HasFlag(LayoutCapabilities.CellInsert));
        Assert.IsFalse(manager.Capabilities.HasFlag(LayoutCapabilities.CellDelete));
        Assert.IsFalse(manager.Capabilities.HasFlag(LayoutCapabilities.CellReorder));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellExecute));
        Assert.IsTrue(manager.Capabilities.HasFlag(LayoutCapabilities.CellResize));
    }
}
