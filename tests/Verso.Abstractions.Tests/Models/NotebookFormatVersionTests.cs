namespace Verso.Abstractions.Tests.Models;

[TestClass]
public sealed class NotebookFormatVersionTests
{
    [TestMethod]
    public void TryParse_ValidVersion_Succeeds()
    {
        Assert.IsTrue(NotebookFormatVersion.TryParse("1.1", out var v));
        Assert.AreEqual(new Version(1, 1), v);
    }

    [TestMethod]
    public void TryParse_NullOrBlank_Fails()
    {
        Assert.IsFalse(NotebookFormatVersion.TryParse(null, out _));
        Assert.IsFalse(NotebookFormatVersion.TryParse("   ", out _));
    }

    [TestMethod]
    public void TryParse_Malformed_Fails()
    {
        Assert.IsFalse(NotebookFormatVersion.TryParse("not-a-version", out _));
    }

    [TestMethod]
    public void Current_And_Initial_AreParseable()
    {
        Assert.IsTrue(NotebookFormatVersion.TryParse(NotebookFormatVersion.Current, out _));
        Assert.IsTrue(NotebookFormatVersion.TryParse(NotebookFormatVersion.Initial, out _));
    }
}
