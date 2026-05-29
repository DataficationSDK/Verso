namespace Verso.Abstractions.Tests.Extensions;

[TestClass]
public class LayoutStaticAssetTests
{
    [TestMethod]
    public void Constructor_PreservesAllValues()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var asset = new LayoutStaticAsset("dashboard.css", "text/css", bytes);

        Assert.AreEqual("dashboard.css", asset.AssetId);
        Assert.AreEqual("text/css", asset.ContentType);
        Assert.AreSame(bytes, asset.Content);
    }

    [TestMethod]
    public void Equals_SameValues_AreEqualReferenceSemanticsForBytes()
    {
        // Record equality on byte[] uses reference equality, not value equality.
        // The contract does not promise content-hash equality; consumers cache by
        // AssetId, not by the asset record as a key. This test pins that semantic
        // so a future change to value-equality is a deliberate decision.
        var bytes = new byte[] { 1, 2, 3 };
        var a = new LayoutStaticAsset("a.css", "text/css", bytes);
        var b = new LayoutStaticAsset("a.css", "text/css", bytes);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void Equals_DifferentAssetId_AreNotEqual()
    {
        var bytes = new byte[] { 1 };
        var a = new LayoutStaticAsset("a.css", "text/css", bytes);
        var b = new LayoutStaticAsset("b.css", "text/css", bytes);

        Assert.AreNotEqual(a, b);
    }
}

[TestClass]
public class LayoutHostCapabilitiesTests
{
    [TestMethod]
    public void Constructor_PreservesAllValues()
    {
        var assetTypes = new HashSet<string> { "text/css" };
        var renderFormats = new HashSet<string> { "text/html" };
        var caps = new LayoutHostCapabilities(assetTypes, renderFormats);

        Assert.AreSame(assetTypes, caps.SupportedAssetContentTypes);
        Assert.AreSame(renderFormats, caps.SupportedRenderFormats);
    }

    [TestMethod]
    public void SupportedAssetContentTypes_LookupIsCaseSensitive()
    {
        // The DTO uses StringComparer.Ordinal so engines see the same content types
        // they advertised, byte-for-byte. A layout returning "Text/Css" should not
        // accidentally match a host advertising "text/css".
        var caps = new LayoutHostCapabilities(
            new HashSet<string>(StringComparer.Ordinal) { "text/css" },
            new HashSet<string>(StringComparer.Ordinal) { "text/html" });

        Assert.IsTrue(caps.SupportedAssetContentTypes.Contains("text/css"));
        Assert.IsFalse(caps.SupportedAssetContentTypes.Contains("Text/Css"));
    }
}
