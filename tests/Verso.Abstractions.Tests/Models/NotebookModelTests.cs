namespace Verso.Abstractions.Tests.Models;

[TestClass]
public class NotebookModelTests
{
    [TestMethod]
    public void Defaults_AreCorrect()
    {
        var nb = new NotebookModel();
        Assert.AreEqual(NotebookFormatVersion.Current, nb.FormatVersion);
        Assert.IsNull(nb.Title);
        Assert.IsNull(nb.Created);
        Assert.IsNull(nb.Modified);
        Assert.IsNull(nb.DefaultKernelId);
        Assert.IsNull(nb.ActiveLayoutId);
        Assert.IsNull(nb.PreferredThemeId);
        Assert.AreEqual(0, nb.RequiredExtensions.Count);
        Assert.AreEqual(0, nb.OptionalExtensions.Count);
        Assert.AreEqual(0, nb.Cells.Count);
        Assert.AreEqual(0, nb.Layouts.Count);
    }

    [TestMethod]
    public void Properties_AreMutable()
    {
        var nb = new NotebookModel();
        nb.Title = "My Notebook";
        nb.DefaultKernelId = "csharp";
        Assert.AreEqual("My Notebook", nb.Title);
        Assert.AreEqual("csharp", nb.DefaultKernelId);
    }

    [TestMethod]
    public void Cells_CanBeAddedAndRemoved()
    {
        var nb = new NotebookModel();
        var cell = new CellModel { Source = "Console.WriteLine(42);" };
        nb.Cells.Add(cell);
        Assert.AreEqual(1, nb.Cells.Count);
        Assert.AreEqual("Console.WriteLine(42);", nb.Cells[0].Source);

        nb.Cells.Remove(cell);
        Assert.AreEqual(0, nb.Cells.Count);
    }

    [TestMethod]
    public void RequiredExtensions_IsMutableList()
    {
        var nb = new NotebookModel();
        nb.RequiredExtensions.Add("verso.csharp-kernel");
        nb.RequiredExtensions.Add("verso.markdown");
        Assert.AreEqual(2, nb.RequiredExtensions.Count);
    }

    [TestMethod]
    public void ActiveLayoutId_Getter_ForwardsLayoutIdHalf()
    {
        var nb = new NotebookModel
        {
            ActiveLayout = new LayoutReference("verso.layout.notebook", "notebook")
        };
        Assert.AreEqual("notebook", nb.ActiveLayoutId);
    }

#pragma warning disable CS0618 // exercising the obsolete compatibility setter on purpose
    [TestMethod]
    public void ActiveLayoutId_Setter_ProducesUnqualifiedReference()
    {
        var nb = new NotebookModel { ActiveLayoutId = "dashboard" };

        Assert.IsNotNull(nb.ActiveLayout);
        Assert.AreEqual("dashboard", nb.ActiveLayout.Value.LayoutId);
        Assert.AreEqual(string.Empty, nb.ActiveLayout.Value.ExtensionId);
        Assert.IsTrue(nb.ActiveLayout.Value.IsUnqualified);
    }

    [TestMethod]
    public void ActiveLayoutId_Setter_PreservesExtensionId_WhenLayoutIdUnchanged()
    {
        var nb = new NotebookModel
        {
            ActiveLayout = new LayoutReference("verso.layout.notebook", "notebook")
        };

        // Re-asserting the same id through the legacy setter must not strip the qualifier.
        nb.ActiveLayoutId = "notebook";

        Assert.AreEqual("verso.layout.notebook", nb.ActiveLayout!.Value.ExtensionId);
        Assert.AreEqual("notebook", nb.ActiveLayout.Value.LayoutId);
        Assert.IsFalse(nb.ActiveLayout.Value.IsUnqualified);
    }

    [TestMethod]
    public void ActiveLayoutId_Setter_DowngradesToUnqualified_WhenLayoutIdChanges()
    {
        var nb = new NotebookModel
        {
            ActiveLayout = new LayoutReference("verso.layout.notebook", "notebook")
        };

        nb.ActiveLayoutId = "dashboard";

        Assert.AreEqual(string.Empty, nb.ActiveLayout!.Value.ExtensionId);
        Assert.AreEqual("dashboard", nb.ActiveLayout.Value.LayoutId);
    }

    [TestMethod]
    public void ActiveLayoutId_Setter_Null_ClearsActiveLayout()
    {
        var nb = new NotebookModel
        {
            ActiveLayout = new LayoutReference("verso.layout.notebook", "notebook"),
            ActiveLayoutId = null
        };

        Assert.IsNull(nb.ActiveLayout);
        Assert.IsNull(nb.ActiveLayoutId);
    }
#pragma warning restore CS0618
}
