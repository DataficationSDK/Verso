using System.Diagnostics;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Makes the Python analysis library available to tests that assert on what only it can answer.
/// Provisioning normally runs in the background while a notebook opens; here it is awaited so the
/// assertions do not race it. It installs into the ordinary per-user location, which is a cache:
/// the first run on a machine installs it and later runs reuse it. A machine that cannot install
/// it reports inconclusive, and the standard-library fallbacks such a machine falls back on are
/// covered by the host script's own test suite.
/// </summary>
internal static class AnalysisTools
{
    private static Version? _version;

    /// <summary>
    /// The version of the interpreter the tests run against. It keys the tools directory the host
    /// puts on the subprocess's import path, so the tools have to be installed under the same key.
    /// </summary>
    public static Version InterpreterVersion
    {
        get
        {
            var python = PythonProbe.Require();
            _version ??= TryQueryVersion(python);

            if (_version is null)
                Assert.Inconclusive("The Python interpreter did not report a usable version.");

            return _version!;
        }
    }

    /// <summary>
    /// Install the tools if that is possible, reporting whether they are available afterwards.
    /// Asserts nothing, so it is safe to call from a test's initialization, where an inconclusive
    /// result would be recorded as a failure.
    /// </summary>
    public static async Task<bool> TryEnsureAsync()
    {
        var python = PythonProbe.Find();
        if (python is null)
            return false;

        _version ??= TryQueryVersion(python);
        if (_version is null)
            return false;

        return await JediTools.EnsureProvisionedAsync(
            python, _version, UvUsage.Auto, CancellationToken.None);
    }

    /// <summary>Ends the test as inconclusive when the tools cannot be made available.</summary>
    public static async Task RequireAsync()
    {
        if (!await TryEnsureAsync())
        {
            Assert.Inconclusive(
                "The Python analysis library could not be installed (an offline or restricted " +
                "machine); skipping the assertions that need it.");
        }
    }

    private static Version? TryQueryVersion(string python)
    {
        var psi = new ProcessStartInfo(python)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("import sys; print('%d.%d.%d' % sys.version_info[:3])");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10000);

            return Version.TryParse(output, out var version) ? version : null;
        }
        catch
        {
            return null;
        }
    }
}
