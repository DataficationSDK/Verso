using System.Diagnostics;
using Verso.Python.Host;

namespace Verso.Python.Tests.Host;

/// <summary>
/// End-to-end lifecycle tests that require a Python 3 interpreter on PATH. When none is found they
/// report a missing prerequisite, which keeps the suite green on a machine without Python and fails
/// the build on a server that was supposed to provide one.
/// </summary>
[TestClass]
public sealed class PythonHostIntegrationTests
{
    private static string? _python;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context) => _python = FindPython();

    [TestMethod]
    public async Task Spawn_CompletesHandshake()
    {
        RequirePython();
        await using var host = new PythonHostProcess(_python!, Environment.CurrentDirectory);

        await host.StartAsync();

        Assert.IsTrue(host.IsAlive);
        Assert.IsNotNull(host.Handshake);
        Assert.AreEqual(HostProtocol.ProtocolVersion, host.Handshake!.Protocol);
        StringAssert.StartsWith(host.Handshake.PythonVersion, "3.");
        Assert.AreEqual("CPython", host.Handshake.Implementation);
        Assert.IsTrue(host.ProcessId > 0);
    }

    [TestMethod]
    public async Task Shutdown_ExitsCleanly()
    {
        RequirePython();
        var host = new PythonHostProcess(_python!, Environment.CurrentDirectory);
        await host.StartAsync();
        var pid = host.ProcessId;

        await host.ShutdownAsync();

        Assert.IsFalse(host.IsAlive);
        Assert.IsFalse(IsRunning(pid), "the subprocess should have exited after shutdown");

        await host.DisposeAsync();
    }

    [TestMethod]
    public async Task Respawn_YieldsFreshProcess()
    {
        RequirePython();
        await using var host = new PythonHostProcess(_python!, Environment.CurrentDirectory);
        await host.StartAsync();
        var first = host.ProcessId;

        await host.RespawnAsync();

        Assert.IsTrue(host.IsAlive);
        Assert.AreNotEqual(first, host.ProcessId);
        Assert.IsNotNull(host.Handshake);
    }

    [TestMethod]
    public async Task StartupFailure_SurfacesStderr()
    {
        RequirePython();

        var scriptPath = Path.Combine(Path.GetTempPath(), "verso-stub-" + Guid.NewGuid().ToString("N") + ".py");
        await File.WriteAllTextAsync(
            scriptPath,
            "import sys\nsys.stderr.write('stub failure marker')\nsys.stderr.flush()\nsys.exit(7)\n");
        try
        {
            await using var host = new PythonHostProcess(
                _python!,
                Environment.CurrentDirectory,
                handshakeTimeout: TimeSpan.FromSeconds(5),
                scriptPathOverride: scriptPath);

            var ex = await Assert.ThrowsExceptionAsync<PythonHostException>(async () => await host.StartAsync());
            StringAssert.Contains(ex.Message, "stub failure marker");
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public async Task Spawn_TellsTheSubprocessWhichProcessToWatch()
    {
        // The subprocess watches this pid and leaves when it goes away, which is what stops an
        // interpreter outliving the kernel that started it while a cell is running. Reported back
        // through the only channel a stand-in script has before a handshake.
        RequirePython();

        var scriptPath = Path.Combine(Path.GetTempPath(), "verso-stub-" + Guid.NewGuid().ToString("N") + ".py");
        await File.WriteAllTextAsync(
            scriptPath,
            "import os, sys\n"
            + "sys.stderr.write('parent=' + os.environ.get('VERSO_PYHOST_PARENT', 'unset'))\n"
            + "sys.stderr.flush()\nsys.exit(7)\n");
        try
        {
            await using var host = new PythonHostProcess(
                _python!,
                Environment.CurrentDirectory,
                handshakeTimeout: TimeSpan.FromSeconds(5),
                scriptPathOverride: scriptPath);

            var ex = await Assert.ThrowsExceptionAsync<PythonHostException>(async () => await host.StartAsync());
            StringAssert.Contains(ex.Message, $"parent={Environment.ProcessId}");
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* best effort */ }
        }
    }

    private static void RequirePython()
    {
        if (_python is null)
            Prerequisite.Missing("Python 3 was not found on PATH.");
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
    }

    private static string? FindPython()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "python", "python3" }
            : new[] { "python3", "python" };

        foreach (var candidate in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process is null) continue;
                process.WaitForExit(5000);
                if (process.HasExited && process.ExitCode == 0)
                    return candidate;
            }
            catch
            {
                // Try the next candidate.
            }
        }
        return null;
    }
}
