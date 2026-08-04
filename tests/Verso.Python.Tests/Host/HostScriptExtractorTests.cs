using System.Text;
using Verso.Python.Host;

namespace Verso.Python.Tests.Host;

[TestClass]
public sealed class HostScriptExtractorTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
        => _root = Path.Combine(Path.GetTempPath(), "verso-host-tests-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
    }

    [TestMethod]
    public void EnsureExtracted_WritesScriptUnderVersionedRoot()
    {
        var path = HostScriptExtractor.EnsureExtracted(_root);

        Assert.IsTrue(File.Exists(path));
        Assert.AreEqual("versohost.py", Path.GetFileName(path));
        StringAssert.StartsWith(path, _root);

        var content = File.ReadAllText(path, Encoding.UTF8);
        StringAssert.Contains(content, "VERSO_PYHOST_TOKEN");
        StringAssert.Contains(content, "def main");
    }

    /// <summary>
    /// Every embedded host script has to be written out, and the list of what to write is kept by
    /// hand. A module packed but not listed is missing only when the script that imports it runs,
    /// which is a failed session rather than a failed build, so the two lists are compared here.
    /// </summary>
    [TestMethod]
    public void EnsureExtracted_WritesEveryEmbeddedHostScript()
    {
        const string prefix = "Verso.Python.Host.";

        var packed = typeof(HostScriptExtractor).Assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".py", StringComparison.Ordinal))
            .Select(name => name.Substring(prefix.Length))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var directory = Path.GetDirectoryName(HostScriptExtractor.EnsureExtracted(_root))!;

        var written = Directory.GetFiles(directory, "*.py")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(packed, written);
    }

    [TestMethod]
    public void EnsureExtracted_IsIdempotent()
    {
        var first = HostScriptExtractor.EnsureExtracted(_root);
        var writtenAt = File.GetLastWriteTimeUtc(first);

        var second = HostScriptExtractor.EnsureExtracted(_root);

        Assert.AreEqual(first, second);
        Assert.AreEqual(writtenAt, File.GetLastWriteTimeUtc(second), "identical content should not be rewritten");
    }

    [TestMethod]
    public void EnsureExtracted_RestoresModifiedScript()
    {
        var path = HostScriptExtractor.EnsureExtracted(_root);
        File.WriteAllText(path, "tampered");

        var again = HostScriptExtractor.EnsureExtracted(_root);

        Assert.AreEqual(path, again);
        StringAssert.Contains(File.ReadAllText(again), "def main", "a modified script should be restored");
    }
}
