namespace Verso.Abstractions.Tests.Models;

[TestClass]
public class NotebookModelTests
{
    [TestMethod]
    public void Defaults_AreCorrect()
    {
        var nb = new NotebookModel();
        Assert.AreEqual("1.0", nb.FormatVersion);
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
}
