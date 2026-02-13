namespace Verso.Abstractions.Tests.Models;

[TestClass]
public class SyntaxColorMapTests
{
    [TestMethod]
    public void SetAndGet_Works()
    {
        var map = new SyntaxColorMap();
        map.Set("keyword", "#0000FF");
        Assert.AreEqual("#0000FF", map.Get("keyword"));
    }

    [TestMethod]
    public void Get_ReturnsNull_ForMissingKey()
    {
        var map = new SyntaxColorMap();
        Assert.IsNull(map.Get("nonexistent"));
    }

    [TestMethod]
    public void Set_OverwritesExisting()
    {
        var map = new SyntaxColorMap();
        map.Set("keyword", "#0000FF");
        map.Set("keyword", "#FF0000");
        Assert.AreEqual("#FF0000", map.Get("keyword"));
        Assert.AreEqual(1, map.Count);
    }

    [TestMethod]
    public void GetAll_ReturnsAllEntries()
    {
        var map = new SyntaxColorMap();
        map.Set("keyword", "#0000FF");
        map.Set("string", "#008000");
        var all = map.GetAll();
        Assert.AreEqual(2, all.Count);
        Assert.AreEqual("#0000FF", all["keyword"]);
        Assert.AreEqual("#008000", all["string"]);
    }

    [TestMethod]
    public void Count_TracksEntries()
    {
        var map = new SyntaxColorMap();
        Assert.AreEqual(0, map.Count);
        map.Set("a", "#111");
        Assert.AreEqual(1, map.Count);
        map.Set("b", "#222");
        Assert.AreEqual(2, map.Count);
    }
}
