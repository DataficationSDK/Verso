using Verso.Extensions.Formatters;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class CollectionFormatterTests
{
    private readonly CollectionFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.formatter.collection", _formatter.ExtensionId);

    [TestMethod]
    public void Priority_IsTen()
        => Assert.AreEqual(10, _formatter.Priority);

    [TestMethod]
    public void CanFormat_List_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new List<int> { 1, 2, 3 }, _context));

    [TestMethod]
    public void CanFormat_Array_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new[] { 1, 2, 3 }, _context));

    [TestMethod]
    public void CanFormat_String_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat("hello", _context));

    [TestMethod]
    public void CanFormat_Object_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(new object(), _context));

    [TestMethod]
    public async Task FormatAsync_TypedList_ProducesHtmlTable()
    {
        var items = new List<TestItem>
        {
            new() { Name = "Alice", Age = 30 },
            new() { Name = "Bob", Age = 25 }
        };

        var result = await _formatter.FormatAsync(items, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("verso-obj-tree"));
        Assert.IsTrue(result.Content.Contains("<details"));
        Assert.IsTrue(result.Content.Contains("Name</th>"));
        Assert.IsTrue(result.Content.Contains("Age</th>"));
        Assert.IsTrue(result.Content.Contains("Alice"));
        Assert.IsTrue(result.Content.Contains("Bob"));
    }

    [TestMethod]
    public async Task FormatAsync_EmptyCollection_ShowsMessage()
    {
        var result = await _formatter.FormatAsync(new List<int>(), _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("Empty collection"));
    }

    [TestMethod]
    public async Task FormatAsync_PrimitiveList_UsesSingleValueColumn()
    {
        var result = await _formatter.FormatAsync(new List<int> { 1, 2, 3 }, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("Value</th>"));
        Assert.IsTrue(result.Content.Contains(">1<"));
        Assert.IsTrue(result.Content.Contains(">2<"));
        Assert.IsTrue(result.Content.Contains(">3<"));
    }

    [TestMethod]
    public async Task FormatAsync_TruncatesAt100Rows()
    {
        var items = Enumerable.Range(1, 150).ToList();
        var result = await _formatter.FormatAsync(items, _context);
        Assert.IsTrue(result.Content.Contains("verso-obj-footer"));
        Assert.IsTrue(result.Content.Contains("Showing 100"));
    }

    [TestMethod]
    public async Task FormatAsync_AnonymousTypes_ShowsProperties()
    {
        var items = new[]
        {
            new { X = 1, Y = "a" },
            new { X = 2, Y = "b" }
        };

        var result = await _formatter.FormatAsync(items, _context);
        Assert.IsTrue(result.Content.Contains("X</th>"));
        Assert.IsTrue(result.Content.Contains("Y</th>"));
    }

    [TestMethod]
    public async Task FormatAsync_TypeWithPublicFields_IncludesFields()
    {
        var items = new[] { new ItemWithField { Name = "test", Tag = "my-tag" } };

        var result = await _formatter.FormatAsync(items, _context);
        Assert.IsTrue(result.Content.Contains("Name</th>"));
        Assert.IsTrue(result.Content.Contains("Tag</th>"));
        Assert.IsTrue(result.Content.Contains("test"));
        Assert.IsTrue(result.Content.Contains("my-tag"));
    }

    [TestMethod]
    public async Task FormatAsync_TypeWithMixedVisibility_ExcludesPrivateMembers()
    {
        var items = new[] { new ItemWithMixedVisibility() };

        var result = await _formatter.FormatAsync(items, _context);
        Assert.IsTrue(result.Content.Contains("Visible</th>"));
        Assert.IsTrue(result.Content.Contains("PublicTag</th>"));
        Assert.IsFalse(result.Content.Contains("Hidden"));
        Assert.IsFalse(result.Content.Contains("_secret"));
    }

    public sealed class TestItem
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    public sealed class ItemWithField
    {
        public string Name { get; set; } = "";
        public string Tag = "";
    }

    public sealed class ItemWithMixedVisibility
    {
        public string Visible { get; set; } = "yes";
        private string Hidden { get; set; } = "no";
        public string PublicTag = "visible-field";
        private int _secret = 42;
    }
}
