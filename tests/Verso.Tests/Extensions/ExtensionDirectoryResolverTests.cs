using Verso.Extensions;

namespace Verso.Tests.Extensions;

[TestClass]
public class ExtensionDirectoryResolverTests
{
    [TestMethod]
    public void GetDefaultManagedDir_EndsWithExtensions()
    {
        var dir = ExtensionDirectoryResolver.GetDefaultManagedDir();
        Assert.IsTrue(dir.EndsWith("extensions", StringComparison.OrdinalIgnoreCase), dir);
    }

    [TestMethod]
    public void Resolve_Null_ReturnsManagedDirOnly()
    {
        var result = ExtensionDirectoryResolver.Resolve(null, out var managedDir);

        Assert.AreEqual(ExtensionDirectoryResolver.GetDefaultManagedDir(), managedDir);
        CollectionAssert.Contains(result.ToList(), managedDir);
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Resolve_ConfiguredDirs_ComeBeforeManagedDir()
    {
        var configured = new[] { "/tmp/exts-a", "/tmp/exts-b" };

        var result = ExtensionDirectoryResolver.Resolve(configured, out var managedDir);

        Assert.AreEqual("/tmp/exts-a", result[0]);
        Assert.AreEqual("/tmp/exts-b", result[1]);
        Assert.AreEqual(managedDir, result[^1]);
    }

    [TestMethod]
    public void Resolve_DuplicateConfiguredDirs_AreDeduplicated()
    {
        var configured = new[] { "/tmp/exts", "/tmp/exts", "/TMP/EXTS" };

        var result = ExtensionDirectoryResolver.Resolve(configured, out _);

        Assert.AreEqual(2, result.Count, "case-insensitive dedup keeps one configured dir + the managed dir");
    }

    [TestMethod]
    public void Resolve_NullAndWhitespaceEntries_AreSkipped()
    {
        var configured = new[] { "/tmp/exts", "", "   ", null };

        var result = ExtensionDirectoryResolver.Resolve(configured, out var managedDir);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("/tmp/exts", result[0]);
        Assert.AreEqual(managedDir, result[1]);
    }
}
