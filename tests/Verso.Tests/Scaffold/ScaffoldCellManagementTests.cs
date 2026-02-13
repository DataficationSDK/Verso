namespace Verso.Tests.Scaffold;

[TestClass]
public sealed class ScaffoldCellManagementTests
{
    private Verso.Scaffold _scaffold = null!;

    [TestInitialize]
    public void Setup() => _scaffold = new Verso.Scaffold();

    [TestMethod]
    public void AddCell_Appends_To_Collection()
    {
        var cell = _scaffold.AddCell();
        Assert.AreEqual(1, _scaffold.Cells.Count);
        Assert.AreEqual(cell.Id, _scaffold.Cells[0].Id);
    }

    [TestMethod]
    public void AddCell_WithParameters_SetsProperties()
    {
        var cell = _scaffold.AddCell(type: "markdown", language: "md", source: "# Hello");
        Assert.AreEqual("markdown", cell.Type);
        Assert.AreEqual("md", cell.Language);
        Assert.AreEqual("# Hello", cell.Source);
    }

    [TestMethod]
    public void AddCell_Multiple_PreservesOrder()
    {
        var a = _scaffold.AddCell(source: "a");
        var b = _scaffold.AddCell(source: "b");
        var c = _scaffold.AddCell(source: "c");

        var cells = _scaffold.Cells;
        Assert.AreEqual(3, cells.Count);
        Assert.AreEqual(a.Id, cells[0].Id);
        Assert.AreEqual(b.Id, cells[1].Id);
        Assert.AreEqual(c.Id, cells[2].Id);
    }

    [TestMethod]
    public void InsertCell_AtBeginning()
    {
        _scaffold.AddCell(source: "existing");
        var inserted = _scaffold.InsertCell(0, source: "first");
        Assert.AreEqual(inserted.Id, _scaffold.Cells[0].Id);
    }

    [TestMethod]
    public void InsertCell_AtEnd()
    {
        _scaffold.AddCell(source: "a");
        var inserted = _scaffold.InsertCell(1, source: "b");
        Assert.AreEqual(inserted.Id, _scaffold.Cells[1].Id);
    }

    [TestMethod]
    public void InsertCell_InMiddle()
    {
        _scaffold.AddCell(source: "a");
        _scaffold.AddCell(source: "c");
        var inserted = _scaffold.InsertCell(1, source: "b");
        Assert.AreEqual("b", _scaffold.Cells[1].Source);
    }

    [TestMethod]
    public void InsertCell_NegativeIndex_Throws()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => _scaffold.InsertCell(-1));
    }

    [TestMethod]
    public void InsertCell_BeyondCount_Throws()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => _scaffold.InsertCell(1));
    }

    [TestMethod]
    public void RemoveCell_Existing_ReturnsTrue()
    {
        var cell = _scaffold.AddCell();
        Assert.IsTrue(_scaffold.RemoveCell(cell.Id));
        Assert.AreEqual(0, _scaffold.Cells.Count);
    }

    [TestMethod]
    public void RemoveCell_NonExistent_ReturnsFalse()
    {
        Assert.IsFalse(_scaffold.RemoveCell(Guid.NewGuid()));
    }

    [TestMethod]
    public void RemoveCell_PreservesOtherCells()
    {
        var a = _scaffold.AddCell(source: "a");
        var b = _scaffold.AddCell(source: "b");
        var c = _scaffold.AddCell(source: "c");

        _scaffold.RemoveCell(b.Id);

        Assert.AreEqual(2, _scaffold.Cells.Count);
        Assert.AreEqual(a.Id, _scaffold.Cells[0].Id);
        Assert.AreEqual(c.Id, _scaffold.Cells[1].Id);
    }

    [TestMethod]
    public void MoveCell_ForwardInList()
    {
        var a = _scaffold.AddCell(source: "a");
        var b = _scaffold.AddCell(source: "b");
        var c = _scaffold.AddCell(source: "c");

        _scaffold.MoveCell(0, 2);

        Assert.AreEqual(b.Id, _scaffold.Cells[0].Id);
        Assert.AreEqual(c.Id, _scaffold.Cells[1].Id);
        Assert.AreEqual(a.Id, _scaffold.Cells[2].Id);
    }

    [TestMethod]
    public void MoveCell_BackwardInList()
    {
        var a = _scaffold.AddCell(source: "a");
        var b = _scaffold.AddCell(source: "b");
        var c = _scaffold.AddCell(source: "c");

        _scaffold.MoveCell(2, 0);

        Assert.AreEqual(c.Id, _scaffold.Cells[0].Id);
        Assert.AreEqual(a.Id, _scaffold.Cells[1].Id);
        Assert.AreEqual(b.Id, _scaffold.Cells[2].Id);
    }

    [TestMethod]
    public void MoveCell_SameIndex_NoChange()
    {
        var a = _scaffold.AddCell(source: "a");
        _scaffold.MoveCell(0, 0);
        Assert.AreEqual(a.Id, _scaffold.Cells[0].Id);
    }

    [TestMethod]
    public void MoveCell_InvalidFrom_Throws()
    {
        _scaffold.AddCell();
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => _scaffold.MoveCell(-1, 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => _scaffold.MoveCell(1, 0));
    }

    [TestMethod]
    public void MoveCell_InvalidTo_Throws()
    {
        _scaffold.AddCell();
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => _scaffold.MoveCell(0, -1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => _scaffold.MoveCell(0, 1));
    }

    [TestMethod]
    public void GetCell_Existing_ReturnsCell()
    {
        var cell = _scaffold.AddCell(source: "hello");
        var found = _scaffold.GetCell(cell.Id);
        Assert.IsNotNull(found);
        Assert.AreEqual("hello", found.Source);
    }

    [TestMethod]
    public void GetCell_NonExistent_ReturnsNull()
    {
        Assert.IsNull(_scaffold.GetCell(Guid.NewGuid()));
    }

    [TestMethod]
    public void ClearCells_RemovesAll()
    {
        _scaffold.AddCell();
        _scaffold.AddCell();
        _scaffold.ClearCells();
        Assert.AreEqual(0, _scaffold.Cells.Count);
    }

    [TestMethod]
    public void UpdateCellSource_ChangesSource()
    {
        var cell = _scaffold.AddCell(source: "old");
        _scaffold.UpdateCellSource(cell.Id, "new");
        Assert.AreEqual("new", _scaffold.GetCell(cell.Id)!.Source);
    }

    [TestMethod]
    public void UpdateCellSource_NonExistent_Throws()
    {
        Assert.ThrowsException<InvalidOperationException>(
            () => _scaffold.UpdateCellSource(Guid.NewGuid(), "x"));
    }

    [TestMethod]
    public void UpdateCellSource_NullSource_Throws()
    {
        var cell = _scaffold.AddCell();
        Assert.ThrowsException<ArgumentNullException>(
            () => _scaffold.UpdateCellSource(cell.Id, null!));
    }

    [TestMethod]
    public void Cells_ReturnsSnapshot()
    {
        _scaffold.AddCell(source: "a");
        var snapshot = _scaffold.Cells;
        _scaffold.AddCell(source: "b");

        Assert.AreEqual(1, snapshot.Count);
        Assert.AreEqual(2, _scaffold.Cells.Count);
    }
}
