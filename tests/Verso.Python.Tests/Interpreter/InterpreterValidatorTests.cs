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

    [TestMethod]
    public async Task ValidateAsync_IsNotHeldUpByACandidateThatFloodsStandardError()
    {
        // A wrapper script, a broken environment, or a shell profile that prints warnings can fill
        // the error pipe. A candidate whose error output is never read blocks writing to it and the
        // probe then waits out its whole timeout, once for every candidate on the search path.
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("The stand-in candidate is a shell script; skipping on Windows.");

        var directory = Directory.CreateTempSubdirectory("verso-noisy-python-");
        var candidate = Path.Combine(directory.FullName, "python3");

        try
        {
            await File.WriteAllTextAsync(candidate,
                "#!/bin/sh\n" +
                "# Far past any pipe buffer, then the answer the probe is actually asking for.\n" +
                "awk 'BEGIN { while (i++ < 4000) print \"warning: something is not quite right\" }' >&2\n" +
                "echo '{\"version\":\"3.12.1\",\"executable\":\"" + candidate +
                "\",\"implementation\":\"CPython\",\"managed\":false}'\n");

            File.SetUnixFileMode(candidate,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var validator = new InterpreterValidator();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var result = await validator.ValidateAsync(candidate, deadline.Token);

            Assert.AreEqual(InterpreterValidationStatus.Valid, result.Status);
            Assert.AreEqual("3.12.1", result.Info!.RawVersion);
        }
        finally
        {
            try { directory.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
