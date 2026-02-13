using Verso.Extensions.Formatters;
using Verso.Tests.Helpers;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class ExceptionFormatterTests
{
    private readonly ExceptionFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.formatter.exception", _formatter.ExtensionId);

    [TestMethod]
    public void Priority_Is50()
        => Assert.AreEqual(50, _formatter.Priority);

    [TestMethod]
    public void CanFormat_Exception_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new InvalidOperationException("test"), _context));

    [TestMethod]
    public void CanFormat_NonException_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat("not an exception", _context));

    [TestMethod]
    public async Task FormatAsync_IncludesTypeName()
    {
        var ex = new InvalidOperationException("test message");
        var result = await _formatter.FormatAsync(ex, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("verso-exception-type"));
        Assert.IsTrue(result.Content.Contains("InvalidOperationException"));
    }

    [TestMethod]
    public async Task FormatAsync_IncludesMessage()
    {
        var ex = new InvalidOperationException("something went wrong");
        var result = await _formatter.FormatAsync(ex, _context);
        Assert.IsTrue(result.Content.Contains("verso-exception-message"));
        Assert.IsTrue(result.Content.Contains("something went wrong"));
    }

    [TestMethod]
    public async Task FormatAsync_IncludesStackTrace()
    {
        Exception captured;
        try { throw new InvalidOperationException("test"); }
        catch (Exception ex) { captured = ex; }

        var result = await _formatter.FormatAsync(captured, _context);
        Assert.IsTrue(result.Content.Contains("verso-exception-stacktrace"));
    }

    [TestMethod]
    public async Task FormatAsync_RendersInnerExceptionChain()
    {
        var inner = new ArgumentException("inner message");
        var outer = new InvalidOperationException("outer message", inner);

        var result = await _formatter.FormatAsync(outer, _context);
        Assert.IsTrue(result.Content.Contains("InvalidOperationException"));
        Assert.IsTrue(result.Content.Contains("ArgumentException"));
        Assert.IsTrue(result.Content.Contains("inner message"));
        Assert.IsTrue(result.Content.Contains("verso-exception-inner"));
    }

    [TestMethod]
    public async Task FormatAsync_HtmlEncodesScriptTag()
    {
        var ex = new InvalidOperationException("<script>alert('xss')</script>");
        var result = await _formatter.FormatAsync(ex, _context);
        Assert.IsFalse(result.Content.Contains("<script>"));
        Assert.IsTrue(result.Content.Contains("&lt;script&gt;"));
    }
}
