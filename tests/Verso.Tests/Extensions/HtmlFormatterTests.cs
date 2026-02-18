using Verso.Extensions.Formatters;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class HtmlFormatterTests
{
    private readonly HtmlFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.formatter.html", _formatter.ExtensionId);

    [TestMethod]
    public void Priority_Is20()
        => Assert.AreEqual(20, _formatter.Priority);

    [TestMethod]
    public void CanFormat_ObjectWithToHtml_ReturnsTrue()
    {
        var obj = new HtmlRenderable();
        Assert.IsTrue(_formatter.CanFormat(obj, _context));
    }

    [TestMethod]
    public void CanFormat_ObjectWithoutToHtml_ReturnsFalse()
    {
        Assert.IsFalse(_formatter.CanFormat(new object(), _context));
    }

    [TestMethod]
    public void CanFormat_String_ReturnsFalse()
    {
        Assert.IsFalse(_formatter.CanFormat("hello", _context));
    }

    [TestMethod]
    public async Task FormatAsync_InvokesToHtml()
    {
        var obj = new HtmlRenderable();
        var result = await _formatter.FormatAsync(obj, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.AreEqual("<div>Hello</div>", result.Content);
    }

    private sealed class HtmlRenderable
    {
        public string ToHtml() => "<div>Hello</div>";
    }
}
