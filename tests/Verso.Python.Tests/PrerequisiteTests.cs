namespace Verso.Python.Tests;

/// <summary>
/// Covers the rule the rest of this suite leans on. It is worth a test of its own because the
/// failure mode is silence: a gate that reported inconclusive everywhere would leave the build
/// green with the whole out-of-process host unexercised, which is the situation it exists to stop.
/// </summary>
[TestClass]
public sealed class PrerequisiteTests
{
    [TestMethod]
    public void Missing_OnABuildServer_Fails()
    {
        var ex = Assert.ThrowsException<AssertFailedException>(
            () => Prerequisite.Missing("Python 3 was not found on PATH.", onBuildServer: true));

        StringAssert.Contains(ex.Message, "Python 3 was not found on PATH.");
    }

    [TestMethod]
    public void Missing_OnADeveloperMachine_ReportsInconclusive()
    {
        var ex = Assert.ThrowsException<AssertInconclusiveException>(
            () => Prerequisite.Missing("Python 3 was not found on PATH.", onBuildServer: false));

        StringAssert.Contains(ex.Message, "Skipping");
    }
}
