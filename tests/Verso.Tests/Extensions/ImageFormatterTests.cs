using Verso.Extensions.Formatters;
using Verso.Tests.Helpers;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class ImageFormatterTests
{
    private readonly ImageFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.formatter.image", _formatter.ExtensionId);

    [TestMethod]
    public void Priority_Is15()
        => Assert.AreEqual(15, _formatter.Priority);

    [TestMethod]
    public void CanFormat_ByteArray_ReturnsTrue()
        => Assert.IsTrue(_formatter.CanFormat(new byte[] { 1, 2, 3 }, _context));

    [TestMethod]
    public void CanFormat_NonByteArray_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat("not bytes", _context));

    [TestMethod]
    public void CanFormat_IntArray_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(new int[] { 1, 2, 3 }, _context));

    [TestMethod]
    public async Task FormatAsync_ProducesBase64ImgTag()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
        var result = await _formatter.FormatAsync(bytes, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("<img "));
        Assert.IsTrue(result.Content.Contains("data:image/png;base64,"));

        var expectedBase64 = Convert.ToBase64String(bytes);
        Assert.IsTrue(result.Content.Contains(expectedBase64));
    }
}
