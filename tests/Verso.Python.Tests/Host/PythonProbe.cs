using System.Diagnostics;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Locates a Python 3 interpreter for the integration tests. Tests that need one report
/// inconclusive when none is present, so the suite stays green on machines without Python.
/// </summary>
internal static class PythonProbe
{
    private static string? _cached;
    private static bool _probed;

    public static string? Find()
    {
        if (_probed) return _cached;
        _probed = true;

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
                {
                    _cached = candidate;
                    return _cached;
                }
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return _cached;
    }

    /// <summary>Returns the interpreter, or ends the test when there is none.</summary>
    public static string Require()
    {
        var python = Find();
        if (python is null)
            Prerequisite.Missing("Python 3 was not found on PATH.");
        return python!;
    }
}
