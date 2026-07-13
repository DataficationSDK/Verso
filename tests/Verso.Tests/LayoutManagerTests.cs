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
        Assert.IsNull(manager.MissingLayoutId);
    }

    [TestMethod]
    public void Constructor_WithUnknownDefaultLayoutId_TracksMissingIdInsteadOfThrowing()
    {
        // A notebook can reference a layout that ships in an extension which
        // hasn't been loaded yet (e.g. via #!extension). Construction must succeed
        // so the host can surface a banner instead of failing the whole open.
        var manager = new LayoutManager(new ILayoutEngine[] { new NotebookLayout() }, "dashboard");

        Assert.IsNull(manager.ActiveLayout);
        Assert.AreEqual("dashboard", manager.MissingLayoutId);
    }

    [TestMethod]
    public void Refresh_MissingDefaultArrives_ActivatesRequestedLayout()
    {
        // A required extension can finish loading after the manager was built. When its
        // layout registers, the notebook should land on the layout it asked for, not the
        // fallback, and the missing marker should clear.
        var notebook = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook }, "dashboard");
        Assert.AreEqual("dashboard", manager.MissingLayoutId);

        var dashboard = new DashboardLayout();
        manager.Refresh(new ILayoutEngine[] { notebook, dashboard });

        Assert.AreSame(dashboard, manager.ActiveLayout);
        Assert.IsNull(manager.MissingLayoutId);
    }

    [TestMethod]
    public void Refresh_AfterExplicitActivation_DoesNotRevertToMissingDefault()
    {
        // Once the user picked a layout themselves, a late-arriving default must not
        // steal the active layout back.
        var notebook = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook }, "dashboard");
        Assert.IsTrue(manager.TryActivate("notebook"));

        var dashboard = new DashboardLayout();
        manager.Refresh(new ILayoutEngine[] { notebook, dashboard });

        Assert.AreSame(notebook, manager.ActiveLayout);
        Assert.IsNull(manager.MissingLayoutId);
    }

    [TestMethod]
    public void TryActivate_KnownId_ReturnsTrueAndClearsMissingId()
    {
        var notebook = new NotebookLayout();
        var manager = new LayoutManager(new ILayoutEngine[] { notebook }, "dashboard");
        Assert.AreEqual("dashboard", manager.MissingLayoutId);

        var activated = manager.TryActivate("notebook");

        Assert.IsTrue(activated);
        Assert.AreSame(notebook, manager.ActiveLayout);
        Assert.IsNull(manager.MissingLayoutId);
    }

    [TestMethod]
    public void TryActivate_UnknownId_ReturnsFalseAndDoesNotThrow()
    {
        var manager = new LayoutManager(new ILayoutEngine[] { new NotebookLayout() }, "notebook");

        var activated = manager.TryActivate("nonexistent");

        Assert.IsFalse(activated);
        Assert.IsNotNull(manager.ActiveLayout);
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

        // Notebook layout (custom renderer: portals live cells into its own HTML)
        manager.SetActiveLayout("notebook");
        Assert.IsTrue(manager.RequiresCustomRenderer);

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
