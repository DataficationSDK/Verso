using Verso.Ado.CellType;
using Verso.Ado.Kernel;

namespace Verso.Ado.Tests.CellType;

[TestClass]
public sealed class SqlCellTypeTests
{
    private readonly SqlCellType _cellType = new();

    [TestMethod]
    public void CellTypeId_IsSql()
        => Assert.AreEqual("sql", _cellType.CellTypeId);

    [TestMethod]
    public void DisplayName_IsSql()
        => Assert.AreEqual("SQL", _cellType.DisplayName);

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.ado.celltype.sql", _cellType.ExtensionId);

    [TestMethod]
    public void Icon_IsNotNull()
        => Assert.IsNotNull(_cellType.Icon);

    [TestMethod]
    public void IsEditable_ReturnsTrue()
        => Assert.IsTrue(_cellType.IsEditable);

    [TestMethod]
    public void GetDefaultContent_ReturnsExpectedTemplate()
    {
        var content = _cellType.GetDefaultContent();
        Assert.IsTrue(content.Contains("SELECT"));
        Assert.IsTrue(content.Contains("--"));
    }

    [TestMethod]
    public void Renderer_IsNotNull_AndIsSqlCellRenderer()
    {
        Assert.IsNotNull(_cellType.Renderer);
        Assert.IsInstanceOfType(_cellType.Renderer, typeof(SqlCellRenderer));
    }

    [TestMethod]
    public void Kernel_IsNotNull_AndIsSqlKernel()
    {
        Assert.IsNotNull(_cellType.Kernel);
        Assert.IsInstanceOfType(_cellType.Kernel, typeof(SqlKernel));
    }
}
