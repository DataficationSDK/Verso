namespace Verso.Abstractions.Tests.Models;

[TestClass]
public class CellOutputTests
{
    [TestMethod]
    public void Constructor_SetsRequiredProperties()
    {
        var output = new CellOutput("text/plain", "hello");
        Assert.AreEqual("text/plain", output.MimeType);
        Assert.AreEqual("hello", output.Content);
    }

    [TestMethod]
    public void Defaults_AreCorrect()
    {
        var output = new CellOutput("text/plain", "hello");
        Assert.IsFalse(output.IsError);
        Assert.IsNull(output.ErrorName);
        Assert.IsNull(output.ErrorStackTrace);
    }

    [TestMethod]
    public void ErrorOutput_SetsAllFields()
    {
        var output = new CellOutput("text/plain", "err", IsError: true, ErrorName: "RuntimeError", ErrorStackTrace: "at line 1");
        Assert.IsTrue(output.IsError);
        Assert.AreEqual("RuntimeError", output.ErrorName);
        Assert.AreEqual("at line 1", output.ErrorStackTrace);
    }

    [TestMethod]
    public void RecordEquality_Works()
    {
        var a = new CellOutput("text/plain", "x");
        var b = new CellOutput("text/plain", "x");
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void WithExpression_CreatesModifiedCopy()
    {
        var a = new CellOutput("text/plain", "x");
        var b = a with { Content = "y" };
        Assert.AreEqual("y", b.Content);
        Assert.AreEqual("x", a.Content);
    }
}
