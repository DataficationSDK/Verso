namespace MyExtension.Tests;

[TestClass]
public sealed class SampleCellTypeTests
{
    private readonly SampleCellType _cellType = new();

    [TestMethod]
    public void ExtensionId_IsSet()
    {
        Assert.IsFalse(string.IsNullOrEmpty(_cellType.ExtensionId));
    }

    [TestMethod]
    public void CellTypeId_IsSet()
    {
        Assert.IsFalse(string.IsNullOrEmpty(_cellType.CellTypeId));
    }

    [TestMethod]
    public void IsEditable_ReturnsTrue()
    {
        Assert.IsTrue(_cellType.IsEditable);
    }

    [TestMethod]
    public void GetDefaultContent_ReturnsNonEmpty()
    {
        var content = _cellType.GetDefaultContent();
        Assert.IsFalse(string.IsNullOrEmpty(content));
    }
}
