using Verso.Extensions.Marketplace;

namespace Verso.Tests.Extensions;

/// <summary>
/// Covers reading a package's own icon back out of the managed extension directory. The icon is
/// read locally rather than fetched from a feed, so these tests only ever touch disk.
/// </summary>
[TestClass]
public class NuGetMarketplacePackageIconTests
{
    private static readonly byte[] PngHeader =
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly byte[] JpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

    private string _managedDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _managedDir = Path.Combine(Path.GetTempPath(), $"verso-icon-test-{Guid.NewGuid():N}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_managedDir))
                Directory.Delete(_managedDir, recursive: true);
        }
        catch { /* best effort */ }
    }

    private static byte[] WithHeader(byte[] header, params byte[] rest)
    {
        var bytes = new byte[header.Length + rest.Length];
        header.CopyTo(bytes, 0);
        rest.CopyTo(bytes, header.Length);
        return bytes;
    }

    private void WriteIcon(string packageId, string version, string fileName, byte[] content)
    {
        var dir = Path.Combine(_managedDir, packageId, version);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, fileName), content);
    }

    [TestMethod]
    public void PackageWithNoIcon_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_managedDir, "Sample.Package", "1.0.0"));

        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "Sample.Package", "1.0.0", _managedDir);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void PackageNotInstalled_ReturnsNull()
    {
        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "Never.Installed", "1.0.0", _managedDir);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void PngIcon_IsReturnedAsAPngDataUri()
    {
        WriteIcon("Sample.Package", "1.0.0", "icon.png", WithHeader(PngHeader, 1, 2, 3));

        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "Sample.Package", "1.0.0", _managedDir);

        Assert.IsNotNull(result);
        StringAssert.StartsWith(result, "data:image/png;base64,");
    }

    [TestMethod]
    public void JpegIcon_IsReturnedAsAJpegDataUri()
    {
        WriteIcon("Sample.Package", "1.0.0", "icon.jpg", WithHeader(JpegHeader, 1, 2, 3));

        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "Sample.Package", "1.0.0", _managedDir);

        Assert.IsNotNull(result);
        StringAssert.StartsWith(result, "data:image/jpeg;base64,");
    }

    [TestMethod]
    public void MediaTypeComesFromContentNotTheFileName()
    {
        // A package controls both the file name and the bytes. Trusting the extension would let
        // it get arbitrary content inlined into the page under an image media type.
        WriteIcon("Sample.Package", "1.0.0", "icon.png",
            System.Text.Encoding.UTF8.GetBytes("<script>alert(1)</script>"));

        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "Sample.Package", "1.0.0", _managedDir);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void EmptyIconFile_IsTreatedAsAbsent()
    {
        WriteIcon("Sample.Package", "1.0.0", "icon.png", Array.Empty<byte>());

        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "Sample.Package", "1.0.0", _managedDir);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void OversizedIcon_IsTreatedAsAbsent()
    {
        // Well past the inlining bound, so a single package cannot push megabytes of base64 at
        // the client. The row falls back to its lettered tile instead.
        var oversized = new byte[512 * 1024];
        PngHeader.CopyTo(oversized, 0);
        WriteIcon("Sample.Package", "1.0.0", "icon.png", oversized);

        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "Sample.Package", "1.0.0", _managedDir);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void UnpinnedVersion_UsesTheHighestInstalledVersion()
    {
        WriteIcon("Sample.Package", "1.0.0", "icon.png", WithHeader(PngHeader, 1));
        WriteIcon("Sample.Package", "2.0.0", "icon.jpg", WithHeader(JpegHeader, 2));

        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "Sample.Package", version: null, _managedDir);

        Assert.IsNotNull(result);
        StringAssert.StartsWith(result, "data:image/jpeg;base64,", "2.0.0 should win over 1.0.0.");
    }

    [TestMethod]
    public void UnpinnedVersion_IgnoresDirectoriesThatAreNotVersions()
    {
        WriteIcon("Sample.Package", "1.0.0", "icon.png", WithHeader(PngHeader, 1));
        WriteIcon("Sample.Package", "scratch", "icon.jpg", WithHeader(JpegHeader, 2));

        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "Sample.Package", version: null, _managedDir);

        Assert.IsNotNull(result);
        StringAssert.StartsWith(result, "data:image/png;base64,");
    }

    [TestMethod]
    public void PackageIdThatEscapesTheManagedDirectory_ReturnsNull()
    {
        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "../../etc", "1.0.0", _managedDir);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void VersionThatEscapesTheManagedDirectory_ReturnsNull()
    {
        var result = NuGetMarketplaceService.TryReadPackageIconDataUri(
            "Sample.Package", "../../etc", _managedDir);

        Assert.IsNull(result);
    }
}
