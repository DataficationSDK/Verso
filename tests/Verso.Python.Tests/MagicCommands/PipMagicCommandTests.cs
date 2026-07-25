using Verso.Python.MagicCommands;

namespace Verso.Python.Tests.MagicCommands;

/// <summary>
/// Covers how <c>#!pip</c> builds its install command. The install itself is left to the manual
/// end-to-end run: reaching a package index would make the suite depend on the network.
/// </summary>
[TestClass]
public sealed class PipMagicCommandTests
{
    private const string Interpreter = "/tmp/env/bin/python";

    [TestMethod]
    public void Install_TargetsTheActiveInterpreterThroughPip()
    {
        var command = PipMagicCommand.BuildInstallCommand(Interpreter, "requests", useUv: false);

        Assert.AreEqual(Interpreter, command.FileName);
        CollectionAssert.AreEqual(
            new[] { "-m", "pip", "install", "--no-input", "requests" },
            command.ArgumentList.ToArray());
    }

    [TestMethod]
    public void Install_NamesTheActiveInterpreterWhenUvRuns()
    {
        var command = PipMagicCommand.BuildInstallCommand(Interpreter, "requests", useUv: true);

        Assert.AreEqual("uv", command.FileName);
        CollectionAssert.AreEqual(
            new[] { "pip", "install", "--python", Interpreter, "requests" },
            command.ArgumentList.ToArray());
    }

    [TestMethod]
    public void Install_PassesEveryPackageAndOptionThrough()
    {
        var command = PipMagicCommand.BuildInstallCommand(
            Interpreter, "pandas==2.0 numpy --upgrade", useUv: false);

        CollectionAssert.Contains(command.ArgumentList.ToArray(), "pandas==2.0");
        CollectionAssert.Contains(command.ArgumentList.ToArray(), "numpy");
        CollectionAssert.Contains(command.ArgumentList.ToArray(), "--upgrade");
    }

    [TestMethod]
    public void Install_CapturesOutputSoItCanBeStreamed()
    {
        var command = PipMagicCommand.BuildInstallCommand(Interpreter, "requests", useUv: false);

        Assert.IsTrue(command.RedirectStandardOutput);
        Assert.IsTrue(command.RedirectStandardError);
        Assert.IsFalse(command.UseShellExecute);
    }

    [TestMethod]
    public void SplitArguments_KeepsAQuotedSpecifierTogether()
    {
        var parts = PipMagicCommand.SplitArguments("\"pandas >= 2.0\" numpy");

        CollectionAssert.AreEqual(new[] { "pandas >= 2.0", "numpy" }, parts.ToArray());
    }

    [TestMethod]
    public void SplitArguments_IgnoresRepeatedWhitespace()
    {
        var parts = PipMagicCommand.SplitArguments("  a   b  ");

        CollectionAssert.AreEqual(new[] { "a", "b" }, parts.ToArray());
    }
}
