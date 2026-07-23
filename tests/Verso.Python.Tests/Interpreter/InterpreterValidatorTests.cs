using Verso.Python.Interpreter;

namespace Verso.Python.Tests.Interpreter;

[TestClass]
public sealed class InterpreterValidatorTests
{
    private static string Probe(string version, string implementation, bool managed)
        => "{\"version\":\"" + version + "\",\"executable\":\"/usr/bin/python3\",\"implementation\":\""
           + implementation + "\",\"managed\":" + (managed ? "true" : "false") + "}";

    [TestMethod]
    public void ParseProbeOutput_AcceptsSupportedCPython()
    {
        var result = InterpreterValidator.ParseProbeOutput(Probe("3.13.3", "CPython", false), "/candidate");

        Assert.AreEqual(InterpreterValidationStatus.Valid, result.Status);
        Assert.IsNotNull(result.Info);
        Assert.AreEqual("/usr/bin/python3", result.Info!.Executable);
        Assert.AreEqual("3.13.3", result.Info.RawVersion);
        Assert.AreEqual(new Version(3, 13, 3), result.Info.Version);
        Assert.AreEqual("CPython", result.Info.Implementation);
        Assert.IsFalse(result.Info.IsExternallyManaged);
    }

    [TestMethod]
    public void ParseProbeOutput_FlagsExternallyManaged()
    {
        var result = InterpreterValidator.ParseProbeOutput(Probe("3.11.9", "CPython", true), "/candidate");

        Assert.AreEqual(InterpreterValidationStatus.Valid, result.Status);
        Assert.IsTrue(result.Info!.IsExternallyManaged);
    }

    [TestMethod]
    public void ParseProbeOutput_RejectsTooOld()
    {
        var result = InterpreterValidator.ParseProbeOutput(Probe("3.7.9", "CPython", false), "/candidate");

        Assert.AreEqual(InterpreterValidationStatus.Rejected, result.Status);
        StringAssert.Contains(result.RejectionReason, "3.7.9");
        StringAssert.Contains(result.RejectionReason, "3.8");
    }

    [TestMethod]
    public void ParseProbeOutput_RejectsNonCPython()
    {
        var result = InterpreterValidator.ParseProbeOutput(Probe("3.11.0", "PyPy", false), "/candidate");

        Assert.AreEqual(InterpreterValidationStatus.Rejected, result.Status);
        StringAssert.Contains(result.RejectionReason, "PyPy");
        StringAssert.Contains(result.RejectionReason, "CPython");
    }

    [TestMethod]
    public void ParseProbeOutput_TreatsNonJsonAsNotFound()
    {
        var result = InterpreterValidator.ParseProbeOutput("not python output at all", "/candidate");

        Assert.AreEqual(InterpreterValidationStatus.NotFound, result.Status);
    }

    [TestMethod]
    public void ParseProbeOutput_FallsBackToCandidateWhenExecutableMissing()
    {
        var result = InterpreterValidator.ParseProbeOutput(
            "{\"version\":\"3.12.1\",\"implementation\":\"CPython\",\"managed\":false}", "/candidate/python");

        Assert.AreEqual(InterpreterValidationStatus.Valid, result.Status);
        Assert.AreEqual("/candidate/python", result.Info!.Executable);
    }

    [TestMethod]
    public void TryParseVersion_ParsesNumericPrefixAndIgnoresSuffix()
    {
        Assert.IsTrue(InterpreterValidator.TryParseVersion("3.13.0rc1", out var pre));
        Assert.AreEqual(new Version(3, 13, 0), pre);

        Assert.IsTrue(InterpreterValidator.TryParseVersion("3.8", out var twoPart));
        Assert.AreEqual(new Version(3, 8, 0), twoPart);

        Assert.IsFalse(InterpreterValidator.TryParseVersion("not-a-version", out _));
    }

    [TestMethod]
    public async Task ValidateAsync_ReturnsNotFoundForMissingExecutable()
    {
        var validator = new InterpreterValidator();
        var missing = Path.Combine(Path.GetTempPath(), "verso-no-such-python-" + Guid.NewGuid().ToString("N"));

        var result = await validator.ValidateAsync(missing, CancellationToken.None);

        Assert.AreEqual(InterpreterValidationStatus.NotFound, result.Status);
    }
}
