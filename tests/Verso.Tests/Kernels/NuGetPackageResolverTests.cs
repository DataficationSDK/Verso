using System.IO.Compression;
using NuGet.Versioning;
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
    public void ParseNuGetReference_RangeContainingComma_KeepsWholeRangeAsVersion()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json, [12.0.0,13.0.0)");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("[12.0.0,13.0.0)", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_RangeWithSpaceAfterComma_KeepsWholeRangeAsVersion()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json, [12.0.0, 13.0.0)");

        Assert.IsNotNull(result);
        Assert.AreEqual("[12.0.0, 13.0.0)", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_FloatingVersion_IsKept()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json, 13.*");

        Assert.IsNotNull(result);
        Assert.AreEqual("13.*", result.Value.Version);
    }

    private static readonly NuGetVersion[] AvailableVersions =
        new[] { "1.0.0", "1.2.0", "1.5.0", "2.0.0", "2.0.12", "3.0.0-rc.1", "1.0.0-alpha", "1.0.0-beta" }
            .Select(NuGetVersion.Parse)
            .ToArray();

    private static string? Select(string? spec) =>
        NuGetPackageResolver.SelectVersion(AvailableVersions, spec)?.ToNormalizedString();

    [TestMethod]
    public void SelectVersion_NoSpec_PicksLatestStable()
    {
        Assert.AreEqual("2.0.12", Select(null));
        Assert.AreEqual("2.0.12", Select(""));
    }

    [TestMethod]
    public void SelectVersion_ExactVersion_IsPinned()
    {
        Assert.AreEqual("1.2.0", Select("1.2.0"));
    }

    [TestMethod]
    public void SelectVersion_ExactPrerelease_IsPinned()
    {
        Assert.AreEqual("3.0.0-rc.1", Select("3.0.0-rc.1"));
    }

    [TestMethod]
    public void SelectVersion_ExactVersionNotOffered_ReturnsNull()
    {
        // A plain version is an exact pin, not a minimum, so a newer release does not stand in.
        Assert.IsNull(Select("1.1.0"));
    }

    [TestMethod]
    public void SelectVersion_Star_PicksLatestStable()
    {
        Assert.AreEqual("2.0.12", Select("*"));
    }

    [TestMethod]
    public void SelectVersion_StarWithPrerelease_PicksLatestIncludingPrerelease()
    {
        Assert.AreEqual("3.0.0-rc.1", Select("*-*"));
    }

    [TestMethod]
    public void SelectVersion_FloatingMajor_PicksLatestStableInThatMajor()
    {
        Assert.AreEqual("1.5.0", Select("1.*"));
    }

    [TestMethod]
    public void SelectVersion_FloatingPrereleaseOfAVersion_PrefersTheStableReleaseOfThatVersion()
    {
        // 1.0.0-* matches every 1.0.0 prerelease and 1.0.0 itself; the release ranks highest.
        Assert.AreEqual("1.0.0", Select("1.0.0-*"));
    }

    [TestMethod]
    public void SelectVersion_FloatingPrereleaseOfAVersion_WithoutARelease_PicksLatestPrerelease()
    {
        var available = new[] { "1.0.0-alpha", "1.0.0-beta", "2.0.0" }.Select(NuGetVersion.Parse);

        Assert.AreEqual("1.0.0-beta", NuGetPackageResolver.SelectVersion(available, "1.0.0-*")?.ToNormalizedString());
    }

    [TestMethod]
    public void SelectVersion_BoundedRange_PicksLowestMatch()
    {
        Assert.AreEqual("1.0.0", Select("[1.0.0,2.0.0)"));
    }

    [TestMethod]
    public void SelectVersion_ExclusiveLowerBound_PicksLowestAboveIt()
    {
        Assert.AreEqual("1.2.0", Select("(1.0.0,)"));
    }

    [TestMethod]
    public void SelectVersion_UpperBoundOnly_PicksLowestStableMatch()
    {
        // Prereleases are left out because the range does not name one.
        Assert.AreEqual("1.0.0", Select("(,1.5.0]"));
    }

    [TestMethod]
    public void SelectVersion_RangeNothingSatisfies_ReturnsNull()
    {
        Assert.IsNull(Select("[4.0.0,)"));
    }

    [TestMethod]
    public void SelectVersion_UnreadableSpec_ReturnsNullAndIsNotValid()
    {
        foreach (var spec in new[] { "1.0.0.x", "latest" })
        {
            Assert.IsNull(Select(spec), spec);
            Assert.IsFalse(NuGetPackageResolver.IsValidVersionSpec(spec), spec);
        }
    }

    [TestMethod]
    public void IsValidVersionSpec_AcceptsEmptyExactFloatingAndRanges()
    {
        foreach (var spec in new[] { null, "", "1.2.0", "3.0.0-rc.1", "*", "*-*", "1.*", "1.0.0-*", "[1.0.0,2.0.0)", "(,1.5.0]" })
            Assert.IsTrue(NuGetPackageResolver.IsValidVersionSpec(spec), spec ?? "null");
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

    [TestMethod]
    public async Task ResolvePackageAsync_PackageFirstReachedThroughALongChain_StillResolvesItsDependencies()
    {
        // The root depends on a six-hop chain that ends at Parent, and on Parent directly. The
        // walk follows the chain first, so Parent is first met six levels down and its own
        // dependency, Child, seven. Child is only two levels from the root through the direct
        // edge, and has to be resolved however the walk happens to meet it first.
        var tag = Guid.NewGuid().ToString("N")[..8];
        string Id(string name) => $"VersoDepthProbe.{tag}.{name}";

        var feed = Path.Combine(Path.GetTempPath(), "verso-depth-probe-" + tag);
        Directory.CreateDirectory(feed);

        var chain = Enumerable.Range(1, 5).Select(i => Id("Chain" + i)).ToArray();
        var packages = new List<(string Id, string[] Dependencies)>
        {
            (Id("Root"), new[] { chain[0], Id("Parent") }),
            (Id("Parent"), new[] { Id("Child") }),
            (Id("Child"), Array.Empty<string>()),
        };
        for (var i = 0; i < chain.Length; i++)
            packages.Add((chain[i], new[] { i + 1 < chain.Length ? chain[i + 1] : Id("Parent") }));

        try
        {
            foreach (var (id, deps) in packages)
                BuildMetaPackage(feed, id, deps);

            var resolver = new NuGetPackageResolver();
            resolver.AddSource(feed);

            var result = await resolver.ResolvePackageAsync(Id("Root"), "1.0.0", CancellationToken.None);

            var resolved = result.ResolvedPackages.Select(p => p.Id).ToList();
            CollectionAssert.Contains(resolved, Id("Parent"));
            CollectionAssert.Contains(resolved, Id("Child"),
                "A dependency two levels from the root was dropped because the walk first met its parent deep in a chain.");
        }
        finally
        {
            try { Directory.Delete(feed, recursive: true); } catch { /* best effort */ }
            foreach (var (id, _) in packages)
            {
                try { Directory.Delete(Path.Combine(NuGetPackageResolver.CacheRoot, id), recursive: true); } catch { /* best effort */ }
            }
        }
    }

    private static void BuildMetaPackage(string folder, string id, string[] dependencies)
    {
        var deps = string.Concat(dependencies.Select(d => $"""<dependency id="{d}" version="1.0.0" />"""));
        var path = Path.Combine(folder, $"{id}.1.0.0.nupkg");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var nuspec = new StreamWriter(zip.CreateEntry($"{id}.nuspec").Open());
        nuspec.Write(
            $"""<?xml version="1.0" encoding="utf-8"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{id}</id><version>1.0.0</version><authors>test</authors><description>Depth probe.</description><dependencies><group targetFramework="net8.0">{deps}</group></dependencies></metadata></package>""");
    }
}
