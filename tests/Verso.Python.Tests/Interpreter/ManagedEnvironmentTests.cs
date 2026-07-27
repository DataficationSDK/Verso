using System.Runtime.InteropServices;
using Verso.Python.Interpreter;

namespace Verso.Python.Tests.Interpreter;

[TestClass]
public sealed class ManagedEnvironmentTests
{
    private static InterpreterInfo Base(string executable, string version = "3.13.3", bool managed = true)
        => FakeInterpreterValidator.MakeInfo(executable, version, "CPython", managed);

    [TestMethod]
    public void ComputeEnvironmentPath_IsDeterministicAndUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "verso-envs-a");
        var manager = new ManagedEnvironment(root);
        var info = Base("/usr/local/bin/python3");

        var first = manager.ComputeEnvironmentPath(info);
        var second = manager.ComputeEnvironmentPath(info);

        Assert.AreEqual(first, second);
        StringAssert.StartsWith(first, root);
        StringAssert.Contains(Path.GetFileName(first), "3.13.3-");
    }

    [TestMethod]
    public void ComputeEnvironmentPath_DiffersByBaseExecutable()
    {
        var manager = new ManagedEnvironment(Path.Combine(Path.GetTempPath(), "verso-envs-b"));

        var one = manager.ComputeEnvironmentPath(Base("/opt/python-a/bin/python3"));
        var two = manager.ComputeEnvironmentPath(Base("/opt/python-b/bin/python3"));

        Assert.AreNotEqual(one, two);
    }

    [TestMethod]
    public void GetInterpreterPath_IsPlatformAppropriate()
    {
        var interpreter = ManagedEnvironment.GetInterpreterPath(Path.Combine("x", "env"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.IsTrue(interpreter.EndsWith(Path.Combine("Scripts", "python.exe")), interpreter);
        }
        else
        {
            Assert.IsTrue(interpreter.EndsWith(Path.Combine("bin", "python")), interpreter);
        }
    }

    [TestMethod]
    public void IsUvAvailable_ReturnsWithoutThrowingAndCaches()
    {
        var manager = new ManagedEnvironment(Path.Combine(Path.GetTempPath(), "verso-envs-c"));

        var first = manager.IsUvAvailable();
        var second = manager.IsUvAvailable();

        Assert.AreEqual(first, second);
    }
}
