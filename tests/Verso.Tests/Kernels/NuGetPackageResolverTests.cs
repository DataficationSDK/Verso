using Verso.Kernels;

namespace Verso.Tests.Kernels;

[TestClass]
public sealed class NuGetPackageResolverTests
{
    [TestMethod]
    public void ParseNuGetReference_PackageOnly()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.IsNull(result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_PackageAndVersion_CommaSeparated()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json, 13.0.1");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_PackageAndVersion_SpaceSeparated()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json 13.0.1");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_WithWhitespace_TrimsCorrectly()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("  Newtonsoft.Json , 13.0.1  ");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_EmptyString_ReturnsNull()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseNuGetReference_NullString_ReturnsNull()
    {
        var result = NuGetPackageResolver.ParseNuGetReference(null);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseNuGetReference_WhitespaceOnly_ReturnsNull()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("   ");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseNuGetReference_CommaThenEmpty_VersionIsNull()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json,");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.IsNull(result.Value.Version);
    }

    [TestMethod]
    public void CacheRoot_IncludesRuntimeTfm()
    {
        var expectedTfm = $"net{Environment.Version.Major}.0";

        Assert.IsTrue(
            NuGetPackageResolver.CacheRoot.Contains(expectedTfm),
            $"CacheRoot should contain '{expectedTfm}' to isolate packages by runtime version. Actual: {NuGetPackageResolver.CacheRoot}");
    }

    [TestMethod]
    public void CacheRoot_IsolatesDifferentRuntimeVersions()
    {
        // The cache path must include the TFM so that processes running on
        // different .NET versions (e.g. Host on .NET 10, CLI on .NET 8)
        // don't share extracted package DLLs built for the wrong runtime.
        var path = NuGetPackageResolver.CacheRoot;
        var segments = path.Split(Path.DirectorySeparatorChar);

        // Expect: {tmp}/verso-nuget-packages/net{major}.0
        var cacheIndex = Array.IndexOf(segments, "verso-nuget-packages");
        Assert.IsTrue(cacheIndex >= 0, "CacheRoot should contain 'verso-nuget-packages' segment");
        Assert.IsTrue(cacheIndex + 1 < segments.Length, "TFM segment should follow 'verso-nuget-packages'");
        Assert.IsTrue(
            segments[cacheIndex + 1].StartsWith("net") && segments[cacheIndex + 1].EndsWith(".0"),
            $"Expected TFM segment like 'net8.0' after 'verso-nuget-packages', got '{segments[cacheIndex + 1]}'");
    }
}
