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
