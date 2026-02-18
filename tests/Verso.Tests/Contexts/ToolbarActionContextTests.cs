using Verso.Abstractions;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Contexts;

[TestClass]
public sealed class ToolbarActionContextTests
{
    [TestMethod]
    public void SelectedCellIds_IsAccessible()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var context = new StubToolbarActionContext { SelectedCellIds = ids };

        Assert.AreEqual(2, context.SelectedCellIds.Count);
        Assert.AreEqual(ids[0], context.SelectedCellIds[0]);
    }

    [TestMethod]
    public void NotebookCells_IsAccessible()
    {
        var cells = new[] { new CellModel { Source = "test" } };
        var context = new StubToolbarActionContext { NotebookCells = cells };

        Assert.AreEqual(1, context.NotebookCells.Count);
        Assert.AreEqual("test", context.NotebookCells[0].Source);
    }

    [TestMethod]
    public void ActiveKernelId_IsAccessible()
    {
        var context = new StubToolbarActionContext { ActiveKernelId = "csharp" };
        Assert.AreEqual("csharp", context.ActiveKernelId);
    }

    [TestMethod]
    public void Notebook_Property_IsWired()
    {
        var stub = new StubNotebookOperations();
        var context = new StubToolbarActionContext { Notebook = stub };

        Assert.AreSame(stub, context.Notebook);
    }

    [TestMethod]
    public void Implements_IToolbarActionContext()
    {
        var context = new StubToolbarActionContext();
        Assert.IsInstanceOfType(context, typeof(IToolbarActionContext));
    }

    [TestMethod]
    public void Implements_IVersoContext()
    {
        var context = new StubToolbarActionContext();
        Assert.IsInstanceOfType(context, typeof(IVersoContext));
    }

    [TestMethod]
    public void IVersoContext_Properties_AreAccessible()
    {
        var context = new StubToolbarActionContext();
        Assert.IsNotNull(context.Variables);
        Assert.IsNotNull(context.Theme);
        Assert.IsNotNull(context.ExtensionHost);
        Assert.IsNotNull(context.NotebookMetadata);
        Assert.IsNotNull(context.Notebook);
    }
}
