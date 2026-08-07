using System.Runtime.InteropServices;
using Verso.Kernels;

namespace Verso.Tests.Kernels;

/// <summary>
/// Native search directories accumulate for the life of the process, so a session that
/// references two versions of the same package holds a native library of each version under
/// the same file name. Resolution has to prefer the one matching the assembly asking for it.
/// </summary>
[TestClass]
public sealed class NativeLibraryResolverTests
{
    private static string Dir(params string[] segments) => Path.Combine(segments);

    [TestMethod]
    public void BelongsToPackageVersion_MatchingVersion_ReturnsTrue()
    {
        var nativeDir = Dir("cache", "SkiaSharp.NativeAssets.macOS", "3.119.0", "native");

        Assert.IsTrue(NuGetRuntimeResolver.BelongsToPackageVersion(nativeDir, "3.119.0"));
    }

    [TestMethod]
    public void BelongsToPackageVersion_DifferentVersion_ReturnsFalse()
    {
        var nativeDir = Dir("cache", "SkiaSharp.NativeAssets.macOS", "2.88.8", "native");

        Assert.IsFalse(NuGetRuntimeResolver.BelongsToPackageVersion(nativeDir, "3.119.0"));
    }

    [TestMethod]
    public void BelongsToPackageVersion_TrailingSeparator_StillMatches()
    {
        var nativeDir = Dir("cache", "SkiaSharp.NativeAssets.macOS", "3.119.0", "native")
            + Path.DirectorySeparatorChar;

        Assert.IsTrue(NuGetRuntimeResolver.BelongsToPackageVersion(nativeDir, "3.119.0"));
    }

    [TestMethod]
    public void BelongsToPackageVersion_VersionOnlyInPackageId_DoesNotMatch()
    {
        // The version has to be the directory above native/, not any segment of the path.
        var nativeDir = Dir("cache", "3.119.0", "SkiaSharp.NativeAssets.macOS", "native");

        Assert.IsFalse(NuGetRuntimeResolver.BelongsToPackageVersion(nativeDir, "3.119.0"));
    }

    [TestMethod]
    public void BelongsToPackageVersion_NoParentDirectory_ReturnsFalse()
    {
        Assert.IsFalse(NuGetRuntimeResolver.BelongsToPackageVersion("native", "3.119.0"));
    }

    [TestMethod]
    public void IsRegisteredDirectory_SameDirectory_ReturnsTrue()
    {
        var registered = new[] { Dir("cache", "SkiaSharp", "3.119.0") };

        Assert.IsTrue(NuGetRuntimeResolver.IsRegisteredDirectory(
            registered, Dir("cache", "SkiaSharp", "3.119.0")));
    }

    [TestMethod]
    public void IsRegisteredDirectory_TrailingSeparator_StillMatches()
    {
        var registered = new[] { Dir("cache", "SkiaSharp", "3.119.0") + Path.DirectorySeparatorChar };

        Assert.IsTrue(NuGetRuntimeResolver.IsRegisteredDirectory(
            registered, Dir("cache", "SkiaSharp", "3.119.0")));
    }

    [TestMethod]
    public void IsRegisteredDirectory_DirectoryStartingWithARegisteredOne_DoesNotMatch()
    {
        // A prerelease of the same version is a different directory, and a prefix test
        // would hand it a resolver meant for its neighbour.
        var registered = new[] { Dir("cache", "SkiaSharp", "3.119.0") };

        Assert.IsFalse(NuGetRuntimeResolver.IsRegisteredDirectory(
            registered, Dir("cache", "SkiaSharp", "3.119.0-preview.1")));
    }

    [TestMethod]
    public void IsRegisteredDirectory_UnrelatedDirectory_ReturnsFalse()
    {
        // Assemblies that came from anywhere else, the host's own included, are left alone.
        var registered = new[] { Dir("cache", "SkiaSharp", "3.119.0") };

        Assert.IsFalse(NuGetRuntimeResolver.IsRegisteredDirectory(
            registered, Dir("usr", "local", "share", "verso")));
    }

    [TestMethod]
    public void SharesResolution_OneInCommon_ReturnsTrue()
    {
        // A package directory registered by two resolves shares one of them with a native
        // directory registered by only the second.
        Assert.IsTrue(NuGetRuntimeResolver.SharesResolution(new[] { 1, 2 }, new[] { 2 }));
    }

    [TestMethod]
    public void SharesResolution_NoneInCommon_ReturnsFalse()
    {
        // Two packages that happen to sit in the same cache but were never resolved together
        // have no claim on each other's natives.
        Assert.IsFalse(NuGetRuntimeResolver.SharesResolution(new[] { 1, 2 }, new[] { 3 }));
    }

    [TestMethod]
    public void SharesResolution_EitherSideEmpty_ReturnsFalse()
    {
        Assert.IsFalse(NuGetRuntimeResolver.SharesResolution(Array.Empty<int>(), new[] { 1 }));
        Assert.IsFalse(NuGetRuntimeResolver.SharesResolution(new[] { 1 }, Array.Empty<int>()));
    }

    [TestMethod]
    public void BuildRidFallbacks_EmulatedProcess_NamesTheProcessArchitecture()
    {
        // An x64 build running under emulation on an arm64 machine can only load x64
        // natives. Reporting the machine's architecture would name the ones it cannot load.
        var rids = NuGetRuntimeResolver.BuildRidFallbacks(
            "osx-x64", Architecture.X64, isMacOS: true, isLinux: false, isWindows: false);

        CollectionAssert.AreEqual(new[] { "osx-x64", "osx", "unix" }, rids);
        CollectionAssert.DoesNotContain(rids, "osx-arm64");
    }

    [TestMethod]
    public void BuildRidFallbacks_Musl_PrefersMuslOverGlibc()
    {
        // A musl process cannot load a glibc build, and packages ship both, so the musl
        // entries have to be reached first.
        var rids = NuGetRuntimeResolver.BuildRidFallbacks(
            "linux-musl-x64", Architecture.X64, isMacOS: false, isLinux: true, isWindows: false);

        CollectionAssert.AreEqual(
            new[] { "linux-musl-x64", "linux-musl", "linux-x64", "linux", "unix" }, rids);
    }

    [TestMethod]
    public void BuildRidFallbacks_Glibc_HasNoMuslEntries()
    {
        var rids = NuGetRuntimeResolver.BuildRidFallbacks(
            "linux-x64", Architecture.X64, isMacOS: false, isLinux: true, isWindows: false);

        CollectionAssert.AreEqual(new[] { "linux-x64", "linux", "unix" }, rids);
    }

    [TestMethod]
    public void BuildRidFallbacks_WindowsArm64_NamesTheArm64Rid()
    {
        var rids = NuGetRuntimeResolver.BuildRidFallbacks(
            "win-arm64", Architecture.Arm64, isMacOS: false, isLinux: false, isWindows: true);

        CollectionAssert.AreEqual(new[] { "win-arm64", "win" }, rids);
    }

    [TestMethod]
    public void BuildRidFallbacks_VersionedRuntimeIdentifier_LeadsAndDoesNotDisplaceThePortableRid()
    {
        // Some platforms name themselves more precisely than the portable chain. The exact
        // name is worth trying first, and the portable one still has to follow it.
        var rids = NuGetRuntimeResolver.BuildRidFallbacks(
            "ubuntu.22.04-x64", Architecture.X64, isMacOS: false, isLinux: true, isWindows: false);

        CollectionAssert.AreEqual(
            new[] { "ubuntu.22.04-x64", "linux-x64", "linux", "unix" }, rids);
    }

    [TestMethod]
    public void BuildRidFallbacks_NoRuntimeIdentifier_StillReturnsThePlatformChain()
    {
        var rids = NuGetRuntimeResolver.BuildRidFallbacks(
            null, Architecture.Arm64, isMacOS: true, isLinux: false, isWindows: false);

        CollectionAssert.AreEqual(new[] { "osx-arm64", "osx", "unix" }, rids);
    }

    [TestMethod]
    public void BuildRidFallbacks_UnknownPlatform_FallsBackToUnix()
    {
        var rids = NuGetRuntimeResolver.BuildRidFallbacks(
            null, Architecture.X64, isMacOS: false, isLinux: false, isWindows: false);

        CollectionAssert.AreEqual(new[] { "unix-x64", "unix" }, rids);
    }

    [TestMethod]
    public void BelongsToPackageVersion_OrdersMatchingDirectoryFirst()
    {
        // The ordering the resolver applies: the matching directory wins regardless of the
        // order the packages happened to be registered in, and the rest keep their order.
        var older = Dir("cache", "SkiaSharp.NativeAssets.macOS", "2.88.8", "native");
        var newer = Dir("cache", "SkiaSharp.NativeAssets.macOS", "3.119.0", "native");
        var unrelated = Dir("cache", "SQLitePCLRaw.lib.e_sqlite3", "2.1.11", "native");

        var ordered = new[] { older, unrelated, newer }
            .OrderByDescending(dir => NuGetRuntimeResolver.BelongsToPackageVersion(dir, "3.119.0") ? 1 : 0)
            .ToArray();

        Assert.AreEqual(newer, ordered[0]);
        Assert.AreEqual(older, ordered[1]);
        Assert.AreEqual(unrelated, ordered[2]);
    }
}
