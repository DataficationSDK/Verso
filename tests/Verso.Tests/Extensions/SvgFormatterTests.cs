using Verso.Extensions.Formatters;
using Verso.Tests.Helpers;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class SvgFormatterTests
{
    private readonly SvgFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.formatter.svg", _formatter.ExtensionId);

    [TestMethod]
    public void Priority_Is18()
        => Assert.AreEqual(18, _formatter.Priority);

    [TestMethod]
    public void CanFormat_ValidSvgString_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"50\"/></svg>", _context));

    [TestMethod]
    public void CanFormat_SvgWithLeadingWhitespace_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat("  <svg><rect/></svg>", _context));

    [TestMethod]
    public void CanFormat_SvgCaseInsensitive_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat("<SVG><rect/></SVG>", _context));

    [TestMethod]
    public void CanFormat_NonSvgString_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat("<div>not svg</div>", _context));

    [TestMethod]
    public void CanFormat_NonStringValue_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(42, _context));

    [TestMethod]
    public async Task FormatAsync_WrapsInContainerDiv()
    {
        var svg = "<svg><circle r=\"50\"/></svg>";
        var result = await _formatter.FormatAsync(svg, _context);
        Assert.IsTrue(result.Content.Contains("verso-svg-output"));
        Assert.IsTrue(result.Content.Contains(svg));
    }

    [TestMethod]
    public async Task FormatAsync_ReturnsTextHtmlMimeType()
    {
        var svg = "<svg><circle r=\"50\"/></svg>";
        var result = await _formatter.FormatAsync(svg, _context);
        Assert.AreEqual("text/html", result.MimeType);
    }
}
