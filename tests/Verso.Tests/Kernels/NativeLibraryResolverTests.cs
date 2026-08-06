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
