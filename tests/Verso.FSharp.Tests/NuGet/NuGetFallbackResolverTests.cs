using NuGet.Versioning;
using Verso.FSharp.NuGet;

namespace Verso.FSharp.Tests.NuGet;

[TestClass]
public class NuGetFallbackResolverTests
{
    [TestMethod]
    public void ParseNuGetReference_PackageOnly_ReturnsPackageIdAndNullVersion()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("Newtonsoft.Json");
        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.IsNull(result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_WithCommaVersion_ReturnsPackageIdAndVersion()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("Newtonsoft.Json, 13.0.1");
        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_WithSpaceVersion_ReturnsPackageIdAndVersion()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("Newtonsoft.Json 13.0.1");
        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_Null_ReturnsNull()
    {
        Assert.IsNull(NuGetFallbackResolver.ParseNuGetReference(null));
    }

    [TestMethod]
    public void ParseNuGetReference_Empty_ReturnsNull()
    {
        Assert.IsNull(NuGetFallbackResolver.ParseNuGetReference(""));
    }

    [TestMethod]
    public void ParseNuGetReference_Whitespace_ReturnsNull()
    {
        Assert.IsNull(NuGetFallbackResolver.ParseNuGetReference("  "));
    }

    [TestMethod]
    public void ParseNuGetReference_TrimsWhitespace()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("  Newtonsoft.Json , 13.0.1 ");
        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_RangeContainingComma_KeepsWholeRangeAsVersion()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("Newtonsoft.Json, [12.0.0,13.0.0)");
        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("[12.0.0,13.0.0)", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_RangeWithSpaceAfterComma_KeepsWholeRangeAsVersion()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("Newtonsoft.Json, [12.0.0, 13.0.0)");
        Assert.IsNotNull(result);
        Assert.AreEqual("[12.0.0, 13.0.0)", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_FloatingVersion_IsKept()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("Newtonsoft.Json, 13.*");
        Assert.IsNotNull(result);
        Assert.AreEqual("13.*", result.Value.Version);
    }

    private static readonly NuGetVersion[] AvailableVersions =
        new[] { "1.0.0", "1.2.0", "1.5.0", "2.0.0", "2.0.12", "3.0.0-rc.1", "1.0.0-alpha", "1.0.0-beta" }
            .Select(NuGetVersion.Parse)
            .ToArray();

    private static string? Select(string? spec) =>
        NuGetFallbackResolver.SelectVersion(AvailableVersions, spec)?.ToNormalizedString();

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

        Assert.AreEqual("1.0.0-beta", NuGetFallbackResolver.SelectVersion(available, "1.0.0-*")?.ToNormalizedString());
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
            Assert.IsFalse(NuGetFallbackResolver.IsValidVersionSpec(spec), spec);
        }
    }

    [TestMethod]
    public void IsValidVersionSpec_AcceptsEmptyExactFloatingAndRanges()
    {
        foreach (var spec in new[] { null, "", "1.2.0", "3.0.0-rc.1", "*", "*-*", "1.*", "1.0.0-*", "[1.0.0,2.0.0)", "(,1.5.0]" })
            Assert.IsTrue(NuGetFallbackResolver.IsValidVersionSpec(spec), spec ?? "null");
    }

    [TestMethod]
    public void CacheRoot_IncludesRuntimeTfm()
    {
        var expectedTfm = $"net{Environment.Version.Major}.0";

        Assert.IsTrue(
            NuGetFallbackResolver.CacheRoot.Contains(expectedTfm),
            $"CacheRoot should contain '{expectedTfm}' to isolate packages by runtime version. Actual: {NuGetFallbackResolver.CacheRoot}");
    }

    [TestMethod]
    public void CacheRoot_IsolatesDifferentRuntimeVersions()
    {
        var path = NuGetFallbackResolver.CacheRoot;
        var segments = path.Split(Path.DirectorySeparatorChar).ToList();

        var cacheIndex = segments.IndexOf("verso-nuget-packages");
        Assert.IsTrue(cacheIndex >= 0, "CacheRoot should contain 'verso-nuget-packages' segment");
        Assert.IsTrue(cacheIndex + 1 < segments.Count, "TFM segment should follow 'verso-nuget-packages'");
        Assert.IsTrue(
            segments[cacheIndex + 1].StartsWith("net") && segments[cacheIndex + 1].EndsWith(".0"),
            $"Expected TFM segment like 'net8.0' after 'verso-nuget-packages', got '{segments[cacheIndex + 1]}'");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ResolvePackageAsync_ResolvesRealPackage()
    {
        var resolver = new NuGetFallbackResolver();
        var result = await resolver.ResolvePackageAsync("Newtonsoft.Json", "13.0.3", CancellationToken.None);

        Assert.AreEqual("Newtonsoft.Json", result.PackageId);
        Assert.AreEqual("13.0.3", result.ResolvedVersion);
        Assert.IsTrue(result.AssemblyPaths.Count > 0, "Expected at least one assembly path");
        Assert.IsTrue(
            result.AssemblyPaths.Any(p => p.EndsWith("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase)),
            "Expected Newtonsoft.Json.dll in assembly paths");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ResolvePackageAsync_NonExistentPackage_ThrowsNotFound()
    {
        var resolver = new NuGetFallbackResolver();

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => resolver.ResolvePackageAsync(
                "Verso.ThisPackageDoesNotExist.ZZZZZ", null, CancellationToken.None));

        Assert.IsTrue(ex.Message.Contains("was not found on any configured source"),
            $"Expected 'was not found on any configured source' in message, got: {ex.Message}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ResolvePackageAsync_NonExistentVersion_ThrowsNotFound()
    {
        var resolver = new NuGetFallbackResolver();

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => resolver.ResolvePackageAsync(
                "Newtonsoft.Json", "99.99.99", CancellationToken.None));

        Assert.IsTrue(ex.Message.Contains("was not found on any configured source"),
            $"Expected 'was not found on any configured source' in message, got: {ex.Message}");
    }

    [TestMethod]
    public void TryGetSatelliteCulture_MatchesTheCoreResolver()
    {
        Assert.IsTrue(NuGetFallbackResolver.TryGetSatelliteCulture("lib/net8.0/de/Some.Ext.resources.dll", out var culture));
        Assert.AreEqual("de", culture);
        Assert.IsFalse(NuGetFallbackResolver.TryGetSatelliteCulture("lib/net8.0/Some.Ext.dll", out _));
        Assert.IsFalse(NuGetFallbackResolver.TryGetSatelliteCulture("lib/net8.0/../Some.Ext.resources.dll", out _));
    }

    [TestMethod]
    public void HasFlattenedSatellites_MatchesTheCoreResolver()
    {
        Assert.IsTrue(NuGetFallbackResolver.HasFlattenedSatellites(new[]
        {
            "/cache/Some.Ext/1.0.0/Some.Ext.dll",
            "/cache/Some.Ext/1.0.0/Some.Ext.resources.dll",
        }));

        Assert.IsFalse(NuGetFallbackResolver.HasFlattenedSatellites(new[]
        {
            "/cache/Foo.Resources/1.0.0/Foo.Resources.dll",
        }), "A main assembly named like a satellite, with no owner beside it, is not a satellite.");
    }
}
