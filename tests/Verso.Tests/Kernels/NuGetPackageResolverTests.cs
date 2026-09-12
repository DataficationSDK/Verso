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

        // Expect: {tmp}/verso-nuget-packages/net{major}.0[-{schemaSuffix}]
        var cacheIndex = Array.IndexOf(segments, "verso-nuget-packages");
        Assert.IsTrue(cacheIndex >= 0, "CacheRoot should contain 'verso-nuget-packages' segment");
        Assert.IsTrue(cacheIndex + 1 < segments.Length, "TFM segment should follow 'verso-nuget-packages'");
        var tfmSegment = segments[cacheIndex + 1];
        Assert.IsTrue(
            tfmSegment.StartsWith($"net{Environment.Version.Major}.0"),
            $"Expected TFM segment to start with 'net{Environment.Version.Major}.0' after 'verso-nuget-packages', got '{tfmSegment}'");
    }

    [TestMethod]
    public void TryGetSatelliteCulture_CultureFolderUnderLib_NamesTheCulture()
    {
        Assert.IsTrue(NuGetPackageResolver.TryGetSatelliteCulture("lib/net8.0/de/Some.Ext.resources.dll", out var culture));
        Assert.AreEqual("de", culture);

        Assert.IsTrue(NuGetPackageResolver.TryGetSatelliteCulture("lib/net8.0/zh-Hans/Some.Ext.resources.dll", out culture));
        Assert.AreEqual("zh-Hans", culture);
    }

    [TestMethod]
    public void TryGetSatelliteCulture_OrdinaryAssembly_IsNotASatellite()
    {
        Assert.IsFalse(NuGetPackageResolver.TryGetSatelliteCulture("lib/net8.0/Some.Ext.dll", out _));
        Assert.IsFalse(NuGetPackageResolver.TryGetSatelliteCulture("lib/net8.0/Some.Ext.resources.dll", out _),
            "A resources assembly with no culture folder is left where it is.");
    }

    [TestMethod]
    public void TryGetSatelliteCulture_UnsafeFolderName_IsRejected()
    {
        Assert.IsFalse(NuGetPackageResolver.TryGetSatelliteCulture("lib/net8.0/../Some.Ext.resources.dll", out _));
        Assert.IsFalse(NuGetPackageResolver.TryGetSatelliteCulture("lib/net8.0/de_DE/Some.Ext.resources.dll", out _));
    }

    [TestMethod]
    public void HasFlattenedSatellites_ResourcesBesideTheAssemblies_IsStale()
    {
        // What a cache entry written before culture folders existed looks like: both languages
        // landed in the package root, where the second overwrote the first and neither can load.
        var cached = new[]
        {
            "/cache/Some.Ext/1.0.0/Some.Ext.dll",
            "/cache/Some.Ext/1.0.0/Some.Ext.resources.dll",
        };

        Assert.IsTrue(NuGetPackageResolver.HasFlattenedSatellites(cached));
    }

    [TestMethod]
    public void HasFlattenedSatellites_CultureFoldersOnly_IsCurrent()
    {
        // The current shape. Only the top level is listed, so the satellites under de/ and ja/
        // are absent from the array entirely, and the entry is served straight from cache.
        var cached = new[]
        {
            "/cache/Some.Ext/1.0.0/Some.Ext.dll",
            "/cache/Some.Ext/1.0.0/Some.Ext.Support.dll",
        };

        Assert.IsFalse(NuGetPackageResolver.HasFlattenedSatellites(cached));
        Assert.IsFalse(NuGetPackageResolver.HasFlattenedSatellites(Array.Empty<string>()),
            "A meta-package with no assemblies at all is not stale.");
    }

    [TestMethod]
    public void HasFlattenedSatellites_ResourcesAssemblyWithoutItsOwner_IsNotStale()
    {
        // A package whose main assembly is named like a satellite. Nothing called "Foo.dll"
        // sits beside it, so nothing says the file is a translation, and calling the entry
        // stale would delete the package's only assembly on every resolve.
        var cached = new[]
        {
            "/cache/Foo.Resources/1.0.0/Foo.Resources.dll",
            "/cache/Foo.Resources/1.0.0/Foo.Resources.Support.dll",
        };

        Assert.IsFalse(NuGetPackageResolver.HasFlattenedSatellites(cached));
    }
}
