using Verso.Extensions.Formatters;
using Verso.Testing.Stubs;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class ObjectFormatterTests
{
    private readonly ObjectFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    // --- Test types ---

    private class SimpleClass
    {
        public string Name { get; set; } = "hello";
        public int Value { get; set; } = 42;
    }

    private class ClassWithField
    {
        public string Name { get; set; } = "test";
        public string Tag = "public-field";
    }

    private class ClassWithPrivateOnly
    {
        private string Secret { get; set; } = "hidden";
        private int _count = 5;
    }

    private class ClassWithMixedVisibility
    {
        public string Visible { get; set; } = "yes";
        private string Hidden { get; set; } = "no";
        public int Count = 10;
        private int _secret = 99;
    }

    private record TestRecord(string Name, int Value);

    // --- Identity ---

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.formatter.object", _formatter.ExtensionId);

    [TestMethod]
    public void Priority_Is5()
        => Assert.AreEqual(5, _formatter.Priority);

    // --- CanFormat ---

    [TestMethod]
    public void CanFormat_ClassWithPublicProperties_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new SimpleClass(), _context));

    [TestMethod]
    public void CanFormat_ClassWithPublicField_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new ClassWithField(), _context));

    [TestMethod]
    public void CanFormat_ClassWithMixedVisibility_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new ClassWithMixedVisibility(), _context));

    [TestMethod]
    public void CanFormat_Record_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new TestRecord("a", 1), _context));

    [TestMethod]
    public void CanFormat_ClassWithPrivateOnly_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(new ClassWithPrivateOnly(), _context));

    [TestMethod]
    public void CanFormat_String_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat("hello", _context));

    [TestMethod]
    public void CanFormat_Int_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(42, _context));

    [TestMethod]
    public void CanFormat_Bool_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(true, _context));

    // --- FormatAsync ---

    [TestMethod]
    public async Task FormatAsync_ClassWithProperties_RendersHtmlTable()
    {
        var obj = new SimpleClass { Name = "world", Value = 99 };
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("verso-obj-tree"));
        Assert.IsTrue(result.Content.Contains("<details"));
        Assert.IsTrue(result.Content.Contains("<summary"));
        Assert.IsTrue(result.Content.Contains("Name"));
        Assert.IsTrue(result.Content.Contains("world"));
        Assert.IsTrue(result.Content.Contains("Value"));
        Assert.IsTrue(result.Content.Contains("99"));
    }

    [TestMethod]
    public async Task FormatAsync_ClassWithField_IncludesPublicField()
    {
        var obj = new ClassWithField { Name = "test", Tag = "my-tag" };
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsTrue(result.Content.Contains("Tag"));
        Assert.IsTrue(result.Content.Contains("my-tag"));
    }

    [TestMethod]
    public async Task FormatAsync_MixedVisibility_ShowsOnlyPublicMembers()
    {
        var obj = new ClassWithMixedVisibility();
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsTrue(result.Content.Contains("Visible"));
        Assert.IsTrue(result.Content.Contains("Count"));
        Assert.IsFalse(result.Content.Contains("Hidden"));
        Assert.IsFalse(result.Content.Contains("_secret"));
    }

    [TestMethod]
    public async Task FormatAsync_HtmlEncodesValues()
    {
        var obj = new SimpleClass { Name = "<script>alert('xss')</script>", Value = 1 };
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsFalse(result.Content.Contains("<script>"));
        Assert.IsTrue(result.Content.Contains("&lt;script&gt;"));
    }

    [TestMethod]
    public async Task FormatAsync_NullPropertyValue_RendersEmpty()
    {
        var obj = new SimpleClass { Name = null!, Value = 0 };
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsTrue(result.Content.Contains("verso-obj-null"));
    }

    [TestMethod]
    public async Task FormatAsync_Record_RendersProperties()
    {
        var obj = new TestRecord("Alice", 30);
        var result = await _formatter.FormatAsync(obj, _context);

        Assert.IsTrue(result.Content.Contains("Alice"));
        Assert.IsTrue(result.Content.Contains("30"));
    }
}
