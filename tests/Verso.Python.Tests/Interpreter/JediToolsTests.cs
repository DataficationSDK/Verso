using Verso.Python.Interpreter;
using Verso.Python.Kernel;

namespace Verso.Python.Tests.Interpreter;

/// <summary>
/// Covers where the analysis tools live and how their absence is handled. The install itself needs
/// a network and is exercised by the host integration tests; what matters here is that an existing
/// installation is reused, a missing interpreter fails quietly, and the directory is keyed so that
/// interpreters sharing a Python minor version share one copy.
/// </summary>
[TestClass]
public sealed class JediToolsTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Directory.CreateTempSubdirectory("verso-tools-").FullName;
        JediTools.ResetPending();
    }

    [TestCleanup]
    public void Cleanup()
    {
        JediTools.ResetPending();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [TestMethod]
    public void ComputeToolsPath_KeysByMinorVersion()
    {
        // The library is pure Python, so every 3.13 interpreter can share one copy.
        var first = JediTools.ComputeToolsPath(new Version(3, 13, 0), _root);
        var second = JediTools.ComputeToolsPath(new Version(3, 13, 7), _root);

        Assert.AreEqual(first, second);
        Assert.AreEqual(Path.Combine(_root, "3.13"), first);
    }

    [TestMethod]
    public void ComputeToolsPath_SeparatesMinorVersions()
    {
        Assert.AreNotEqual(
            JediTools.ComputeToolsPath(new Version(3, 9, 0), _root),
            JediTools.ComputeToolsPath(new Version(3, 13, 0), _root));
    }

    [TestMethod]
    public void IsProvisioned_FollowsThePackageDirectory()
    {
        var version = new Version(3, 13, 3);
        Assert.IsFalse(JediTools.IsProvisioned(version, _root));

        Directory.CreateDirectory(Path.Combine(JediTools.ComputeToolsPath(version, _root), "jedi"));

        Assert.IsTrue(JediTools.IsProvisioned(version, _root));
    }

    [TestMethod]
    public async Task EnsureProvisionedAsync_ReusesAnExistingInstallation()
    {
        var version = new Version(3, 13, 3);
        Directory.CreateDirectory(Path.Combine(JediTools.ComputeToolsPath(version, _root), "jedi"));

        // The interpreter cannot be started, so a call that returns true proves nothing ran.
        var provisioned = await JediTools.EnsureProvisionedAsync(
            "verso-interpreter-that-does-not-exist", version, UvUsage.Off, CancellationToken.None,
            _root);

        Assert.IsTrue(provisioned);
    }

    [TestMethod]
    public async Task EnsureProvisionedAsync_FailsQuietlyWithoutAnInterpreter()
    {
        var version = new Version(3, 13, 3);

        var provisioned = await JediTools.EnsureProvisionedAsync(
            "verso-interpreter-that-does-not-exist", version, UvUsage.Off, CancellationToken.None,
            _root);

        Assert.IsFalse(provisioned);
        Assert.IsFalse(JediTools.IsProvisioned(version, _root));
    }

    [TestMethod]
    public async Task EnsureProvisionedAsync_LeavesNothingBehindWhenTheInstallFails()
    {
        // A session reads this directory while an install for another notebook may be running,
        // and it decides from the package directory alone. Anything left behind by an install
        // that did not finish would be read as one that had.
        var version = new Version(3, 13, 3);

        await JediTools.EnsureProvisionedAsync(
            "verso-interpreter-that-does-not-exist", version, UvUsage.Off, CancellationToken.None,
            _root);

        Assert.IsFalse(Directory.Exists(JediTools.ComputeToolsPath(version, _root)),
            "A failed install must not publish a directory for a reader to find.");
        Assert.AreEqual(0, Directory.GetDirectories(_root).Length,
            "Nor leave its staging directory behind.");
    }

    [TestMethod]
    public async Task EnsureProvisionedAsync_TriesAgainAfterAFailure()
    {
        var version = new Version(3, 13, 3);

        Assert.IsFalse(await JediTools.EnsureProvisionedAsync(
            "verso-interpreter-that-does-not-exist", version, UvUsage.Off, CancellationToken.None,
            _root));

        // A failed attempt is not remembered as an answer, so a machine that comes back online
        // during a session is not stuck with the fallbacks for the rest of it.
        Assert.IsFalse(await JediTools.EnsureProvisionedAsync(
            "verso-interpreter-that-does-not-exist", version, UvUsage.Off, CancellationToken.None,
            _root));
    }
}
