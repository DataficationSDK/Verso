using Verso.Python.PackageManagement;

namespace Verso.Python.Tests.PackageManagement;

/// <summary>
/// Covers how the shared installer builds its command. The install itself is covered by the
/// integration tests, which use a local wheel directory so the suite never reaches an index.
/// </summary>
[TestClass]
public sealed class PackageInstallerTests
{
    private const string Interpreter = "/tmp/env/bin/python";

    [TestMethod]
    public void Install_TargetsTheActiveInterpreterThroughPip()
    {
        var command = PackageInstaller.BuildInstallCommand(Interpreter, "requests", useUv: false);

        Assert.AreEqual(Interpreter, command.FileName);
        CollectionAssert.AreEqual(
            new[] { "-m", "pip", "install", "--no-input", "requests" },
            command.ArgumentList.ToArray());
    }

    [TestMethod]
    public void Install_NamesTheActiveInterpreterWhenUvRuns()
    {
        var command = PackageInstaller.BuildInstallCommand(Interpreter, "requests", useUv: true);

        Assert.AreEqual("uv", command.FileName);
        CollectionAssert.AreEqual(
            new[] { "pip", "install", "--python", Interpreter, "requests" },
            command.ArgumentList.ToArray());
    }

    [TestMethod]
    public void Install_PassesEveryPackageAndOptionThrough()
    {
        var command = PackageInstaller.BuildInstallCommand(
            Interpreter, "pandas==2.0 numpy --upgrade", useUv: false);

        CollectionAssert.Contains(command.ArgumentList.ToArray(), "pandas==2.0");
        CollectionAssert.Contains(command.ArgumentList.ToArray(), "numpy");
        CollectionAssert.Contains(command.ArgumentList.ToArray(), "--upgrade");
    }

    [TestMethod]
    public void Install_CapturesOutputSoItCanBeStreamed()
    {
        var command = PackageInstaller.BuildInstallCommand(Interpreter, "requests", useUv: false);

        Assert.IsTrue(command.RedirectStandardOutput);
        Assert.IsTrue(command.RedirectStandardError);
        Assert.IsFalse(command.UseShellExecute);
    }

    [TestMethod]
    public void Install_AcceptsAPreSplitSpecifierList()
    {
        var command = PackageInstaller.BuildInstallCommand(
            Interpreter, new[] { "pandas>=2", "--no-index" }, useUv: false);

        CollectionAssert.Contains(command.ArgumentList.ToArray(), "pandas>=2");
        CollectionAssert.Contains(command.ArgumentList.ToArray(), "--no-index");
    }

    [TestMethod]
    public void SplitArguments_KeepsAQuotedSpecifierTogether()
    {
        var parts = PackageInstaller.SplitArguments("\"pandas >= 2.0\" numpy");

        CollectionAssert.AreEqual(new[] { "pandas >= 2.0", "numpy" }, parts.ToArray());
    }

    [TestMethod]
    public void SplitArguments_IgnoresRepeatedWhitespace()
    {
        var parts = PackageInstaller.SplitArguments("  a   b  ");

        CollectionAssert.AreEqual(new[] { "a", "b" }, parts.ToArray());
    }
}
