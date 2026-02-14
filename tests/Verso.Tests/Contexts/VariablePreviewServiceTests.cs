using Verso.Contexts;
using Verso.Stubs;

namespace Verso.Tests.Contexts;

[TestClass]
public sealed class VariablePreviewServiceTests
{
    private VariablePreviewService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        var stubHost = new StubExtensionHostContext(() => Array.Empty<Verso.Abstractions.ILanguageKernel>());
        _service = new VariablePreviewService(stubHost);
    }

    [TestMethod]
    public void Null_ReturnsNullString()
    {
        Assert.AreEqual("null", _service.GetPreview(null));
    }

    [TestMethod]
    public void Integer_ReturnsToString()
    {
        Assert.AreEqual("42", _service.GetPreview(42));
    }

    [TestMethod]
    public void Double_ReturnsToString()
    {
        var preview = _service.GetPreview(3.14);
        Assert.IsTrue(preview.Contains("3.14"));
    }

    [TestMethod]
    public void Boolean_ReturnsLowerCase()
    {
        Assert.AreEqual("true", _service.GetPreview(true));
        Assert.AreEqual("false", _service.GetPreview(false));
    }

    [TestMethod]
    public void String_ReturnsQuoted()
    {
        Assert.AreEqual("\"hello\"", _service.GetPreview("hello"));
    }

    [TestMethod]
    public void Char_ReturnsQuoted()
    {
        Assert.AreEqual("'A'", _service.GetPreview('A'));
    }

    [TestMethod]
    public void Collection_ShowsCount()
    {
        var list = new List<int> { 1, 2, 3 };
        var preview = _service.GetPreview(list);
        Assert.IsTrue(preview.Contains("Count = 3"), $"Expected count in '{preview}'");
    }

    [TestMethod]
    public void Array_ShowsCount()
    {
        var arr = new int[] { 10, 20 };
        var preview = _service.GetPreview(arr);
        Assert.IsTrue(preview.Contains("Count = 2"), $"Expected count in '{preview}'");
    }

    [TestMethod]
    public void LongString_Truncated()
    {
        var longString = new string('x', 300);
        var preview = _service.GetPreview(longString, maxLength: 50);
        Assert.IsTrue(preview.Length <= 50);
        Assert.IsTrue(preview.EndsWith("..."));
    }

    [TestMethod]
    public void Dictionary_ShowsCount()
    {
        var dict = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var preview = _service.GetPreview(dict);
        Assert.IsTrue(preview.Contains("Count = 2"), $"Expected count in '{preview}'");
    }
}
