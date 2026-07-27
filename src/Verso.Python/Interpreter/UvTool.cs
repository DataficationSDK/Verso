using System.Diagnostics;

namespace Verso.Python.Interpreter;

/// <summary>
/// Detection of the uv tool, which creates environments and installs packages considerably
/// faster than the standard tooling. The probe runs once per process: uv appearing or leaving
/// mid-session is not worth paying for on every install.
/// </summary>
internal static class UvTool
{
    /// <summary>The executable name, resolved through the search path.</summary>
    public const string Executable = "uv";

    private static bool? _available;

    /// <summary>Whether uv is on the search path.</summary>
    public static bool IsAvailable()
    {
        _available ??= Probe();
        return _available.Value;
    }

    /// <summary>Resets the cached probe result. For tests.</summary>
    internal static void ResetProbe() => _available = null;

    private static bool Probe()
    {
        try
        {
            var psi = new ProcessStartInfo(Executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            if (!process.WaitForExit(5000))
            {
                // Disposing the handle would leave the tool running with nothing reading its
                // pipes, which is how a probe turns into a process nobody owns.
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
