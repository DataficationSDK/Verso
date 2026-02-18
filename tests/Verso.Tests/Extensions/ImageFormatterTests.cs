using Verso.Extensions.Formatters;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

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

    [TestMethod]
    public void DetectMimeType_PngMagicBytes_ReturnsPng()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.AreEqual("image/png", ImageFormatter.DetectMimeType(bytes));
    }

    [TestMethod]
    public void DetectMimeType_JpegMagicBytes_ReturnsJpeg()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        Assert.AreEqual("image/jpeg", ImageFormatter.DetectMimeType(bytes));
    }

    [TestMethod]
    public void DetectMimeType_GifMagicBytes_ReturnsGif()
    {
        var bytes = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 };
        Assert.AreEqual("image/gif", ImageFormatter.DetectMimeType(bytes));
    }

    [TestMethod]
    public void DetectMimeType_UnknownBytes_DefaultsToPng()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        Assert.AreEqual("image/png", ImageFormatter.DetectMimeType(bytes));
    }

    [TestMethod]
    public async Task FormatAsync_OutputHasMaxWidth()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var result = await _formatter.FormatAsync(bytes, _context);
        Assert.IsTrue(result.Content.Contains("max-width:100%"));
    }
}
