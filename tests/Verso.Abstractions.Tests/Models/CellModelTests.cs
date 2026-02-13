namespace Verso.Abstractions.Tests.Models;

[TestClass]
public class CellModelTests
{
    [TestMethod]
    public void Defaults_AreCorrect()
    {
        var cell = new CellModel();
        Assert.AreNotEqual(Guid.Empty, cell.Id);
        Assert.AreEqual("code", cell.Type);
        Assert.IsNull(cell.Language);
        Assert.AreEqual("", cell.Source);
        Assert.AreEqual(0, cell.Outputs.Count);
        Assert.AreEqual(0, cell.Metadata.Count);
    }

    [TestMethod]
    public void Id_IsUniquePerInstance()
    {
        var a = new CellModel();
        var b = new CellModel();
        Assert.AreNotEqual(a.Id, b.Id);
    }

    [TestMethod]
    public void Properties_AreMutable()
    {
        var cell = new CellModel();
        cell.Type = "markdown";
        cell.Language = "csharp";
        cell.Source = "# Hello";
        Assert.AreEqual("markdown", cell.Type);
        Assert.AreEqual("csharp", cell.Language);
        Assert.AreEqual("# Hello", cell.Source);
    }

    [TestMethod]
    public void Outputs_CanBeAdded()
    {
        var cell = new CellModel();
        cell.Outputs.Add(new CellOutput("text/plain", "42"));
        Assert.AreEqual(1, cell.Outputs.Count);
        Assert.AreEqual("42", cell.Outputs[0].Content);
    }

    [TestMethod]
    public void Metadata_CanBePopulated()
    {
        var cell = new CellModel();
        cell.Metadata["collapsed"] = true;
        Assert.IsTrue((bool)cell.Metadata["collapsed"]);
    }
}
