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

    [TestMethod]
    public void LoadHints_DefaultIsNull()
    {
        var asset = new LayoutStaticAsset("a.css", "text/css", new byte[] { 1 });
        Assert.IsNull(asset.LoadHints);
        Assert.IsNull(asset.ContentSecurityPolicy);
    }

    [TestMethod]
    public void WithLoadHints_RoundTripsValue()
    {
        var hints = new LayoutStaticAssetLoadHints(
            LayoutScriptModuleKind.Module,
            LayoutScriptLoadMode.Async,
            LayoutScriptPlacement.BeforeLayoutHtml);
        var asset = new LayoutStaticAsset("a.js", "text/javascript", new byte[] { 1 })
        {
            LoadHints = hints,
        };

        Assert.AreSame(hints, asset.LoadHints);
    }

    [TestMethod]
    public void WithCsp_RoundTripsValue()
    {
        var asset = new LayoutStaticAsset("a.js", "text/javascript", new byte[] { 1 })
        {
            ContentSecurityPolicy = "script-src 'self'",
        };

        Assert.AreEqual("script-src 'self'", asset.ContentSecurityPolicy);
    }

    [TestMethod]
    public void Equals_LoadHintsConsideredInEquality()
    {
        var bytes = new byte[] { 1 };
        var hintsA = new LayoutStaticAssetLoadHints(
            LayoutScriptModuleKind.Module, LayoutScriptLoadMode.Async, LayoutScriptPlacement.BeforeLayoutHtml);
        var hintsB = new LayoutStaticAssetLoadHints(
            LayoutScriptModuleKind.Classic, LayoutScriptLoadMode.Defer, LayoutScriptPlacement.AfterLayoutHtml);

        var a = new LayoutStaticAsset("x.js", "text/javascript", bytes) { LoadHints = hintsA };
        var b = new LayoutStaticAsset("x.js", "text/javascript", bytes) { LoadHints = hintsA };
        var c = new LayoutStaticAsset("x.js", "text/javascript", bytes) { LoadHints = hintsB };

        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, c);
    }
}

[TestClass]
public class LayoutStaticAssetLoadHintsTests
{
    [TestMethod]
    public void Defaults_AreClassicDeferAfterLayoutHtml()
    {
        var hints = new LayoutStaticAssetLoadHints();

        Assert.AreEqual(LayoutScriptModuleKind.Classic, hints.ModuleKind);
        Assert.AreEqual(LayoutScriptLoadMode.Defer, hints.LoadMode);
        Assert.AreEqual(LayoutScriptPlacement.AfterLayoutHtml, hints.Placement);
    }

    [TestMethod]
    public void Equals_SameValues_AreEqual()
    {
        var a = new LayoutStaticAssetLoadHints(
            LayoutScriptModuleKind.Module, LayoutScriptLoadMode.Async, LayoutScriptPlacement.BeforeLayoutHtml);
        var b = new LayoutStaticAssetLoadHints(
            LayoutScriptModuleKind.Module, LayoutScriptLoadMode.Async, LayoutScriptPlacement.BeforeLayoutHtml);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void Equals_DifferentValues_AreNotEqual()
    {
        var a = new LayoutStaticAssetLoadHints(LoadMode: LayoutScriptLoadMode.Defer);
        var b = new LayoutStaticAssetLoadHints(LoadMode: LayoutScriptLoadMode.Async);

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
