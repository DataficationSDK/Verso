using System.Runtime.InteropServices;
using Verso.JavaScript.Kernel;

namespace Verso.JavaScript.Tests.Kernel;

/// <summary>
/// Covers Node.js discovery, with emphasis on shim-based version managers (Volta) whose
/// <c>node</c> entry is a symlink to a single dispatcher binary that must be invoked under
/// its <c>node</c> name rather than by resolving the link to the dispatcher.
/// </summary>
[TestClass]
public class NodeDiscoveryTests
{
    private string? _tempDir;

    [TestCleanup]
    public void Cleanup()
    {
        if (_tempDir is not null && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        Environment.SetEnvironmentVariable(JavaScriptEngineManager.NodePathEnvVar, null);
    }

    private string CreateTempDir()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "verso-node-disc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        return _tempDir;
    }

    [TestMethod]
    public void ResolveSymlink_ShimWithDifferentName_KeepsOriginalLinkPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Symlink semantics under test are POSIX shim managers.");
            return;
        }

        var dir = CreateTempDir();

        // Mimic Volta: ~/.volta/bin/node -> volta-shim (a differently named dispatcher).
        var shim = Path.Combine(dir, "volta-shim");
        File.WriteAllText(shim, "#!/bin/sh\n");
        var nodeLink = Path.Combine(dir, "node");
        File.CreateSymbolicLink(nodeLink, shim);

        var result = JavaScriptEngineManager.ResolveSymlink(nodeLink);

        // The link must NOT be followed to the dispatcher, so it stays invokable as "node".
        Assert.AreEqual(nodeLink, result);
    }

    [TestMethod]
    public void ResolveSymlink_LinkToSameName_ResolvesToTarget()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("Symlink semantics under test are POSIX shim managers.");
            return;
        }

        var dir = CreateTempDir();

        // Mimic Homebrew/fnm: a "node" link that targets a real binary still named "node".
        var realDir = Path.Combine(dir, "real");
        Directory.CreateDirectory(realDir);
        var realNode = Path.Combine(realDir, "node");
        File.WriteAllText(realNode, "#!/bin/sh\n");
        var nodeLink = Path.Combine(dir, "node");
        File.CreateSymbolicLink(nodeLink, realNode);

        var result = JavaScriptEngineManager.ResolveSymlink(nodeLink);

        // Same-name target is safe to resolve to a stable path.
        Assert.AreEqual(realNode, result);
    }

    [TestMethod]
    public void ResolveSymlink_PlainFile_ReturnsUnchanged()
    {
        var dir = CreateTempDir();
        var plain = Path.Combine(dir, "node");
        File.WriteAllText(plain, "#!/bin/sh\n");

        Assert.AreEqual(plain, JavaScriptEngineManager.ResolveSymlink(plain));
    }

    [TestMethod]
    public void EnumerateCandidates_EnvVarSet_IsTriedFirst()
    {
        var sentinel = Path.Combine(CreateTempDir(), "my-node");
        Environment.SetEnvironmentVariable(JavaScriptEngineManager.NodePathEnvVar, sentinel);

        var first = JavaScriptEngineManager.EnumerateCandidates().FirstOrDefault();

        Assert.AreEqual(sentinel, first);
    }

    [TestMethod]
    public void EnumerateCandidates_EnvVarWhitespace_IsIgnored()
    {
        Environment.SetEnvironmentVariable(JavaScriptEngineManager.NodePathEnvVar, "   ");

        // A blank override must not become a candidate; discovery falls through to PATH and
        // well-known locations. The override sentinel simply must not appear.
        var candidates = JavaScriptEngineManager.EnumerateCandidates().ToList();

        Assert.IsFalse(candidates.Contains("   "));
        Assert.IsFalse(candidates.Contains(string.Empty));
    }
}
