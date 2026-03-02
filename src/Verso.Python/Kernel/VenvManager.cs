using System.Diagnostics;
using System.Runtime.InteropServices;
using Verso.Abstractions;

namespace Verso.Python.Kernel;

/// <summary>
/// Manages a per-user virtual environment used by <c>#!pip</c> to install packages
/// without polluting the system Python (avoids PEP 668 externally-managed-environment errors).
/// The venv lives at <c>~/.verso/python/venv</c> and is created on first use.
/// </summary>
internal static class VenvManager
{
    internal const string SitePackagesStoreKey = "__verso_pip_site_packages";

    private static readonly string VenvPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".verso", "python", "venv");

    private static string? _cachedSitePackages;
    private static bool _venvReady;

    /// <summary>
    /// Returns the path to the Python executable inside the venv.
    /// </summary>
    internal static string GetPythonPath() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(VenvPath, "Scripts", "python.exe")
            : Path.Combine(VenvPath, "bin", "python3");

    /// <summary>
    /// Ensures the virtual environment exists, creating it if necessary.
    /// </summary>
    internal static async Task<bool> EnsureCreatedAsync(
        string systemPython, IMagicCommandContext context, CancellationToken ct)
    {
        if (_venvReady && File.Exists(GetPythonPath()))
            return true;

        if (File.Exists(GetPythonPath()))
        {
            _venvReady = true;
            return true;
        }

        await context.WriteOutputAsync(new CellOutput(
            "text/plain", $"Creating virtual environment at {VenvPath}..."))
            .ConfigureAwait(false);

        var psi = new ProcessStartInfo(systemPython, $"-m venv \"{VenvPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain", "Failed to start venv creation process.", IsError: true))
                    .ConfigureAwait(false);
                return false;
            }

            var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain", $"Failed to create virtual environment: {stderr.Trim()}", IsError: true))
                    .ConfigureAwait(false);
                return false;
            }

            _venvReady = true;
            return true;
        }
        catch (Exception ex)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", $"Failed to create virtual environment: {ex.Message}", IsError: true))
                .ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>
    /// Silently ensures jedi is installed in the venv. Creates the venv if needed.
    /// Returns true if jedi was successfully installed (or already present).
    /// </summary>
    internal static async Task<bool> EnsureJediInstalledAsync(CancellationToken ct)
    {
        // Create venv if it doesn't exist
        if (!File.Exists(GetPythonPath()))
        {
            var systemPython = PythonEngineManager.FindPython3OnPath();
            if (systemPython is null)
                return false;

            var psi = new ProcessStartInfo(systemPython, $"-m venv \"{VenvPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(psi);
                if (process is null) return false;

                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                if (process.ExitCode != 0) return false;

                _venvReady = true;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            _venvReady = true;
        }

        // Install jedi
        var pipPsi = new ProcessStartInfo(
            GetPythonPath(),
            "-m pip install --quiet --disable-pip-version-check jedi")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var pipProcess = Process.Start(pipPsi);
            if (pipProcess is null) return false;

            await pipProcess.WaitForExitAsync(ct).ConfigureAwait(false);
            return pipProcess.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the venv's site-packages directory by asking the venv's Python.
    /// The result is cached after the first successful call.
    /// </summary>
    internal static async Task<string?> GetSitePackagesPathAsync(CancellationToken ct)
    {
        if (_cachedSitePackages is not null)
            return _cachedSitePackages;

        var pythonPath = GetPythonPath();
        if (!File.Exists(pythonPath))
            return null;

        var psi = new ProcessStartInfo(pythonPath, "-c \"import site; print(site.getsitepackages()[0])\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = (await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false)).Trim();
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                _cachedSitePackages = output;
                return output;
            }
        }
        catch
        {
            // Fall through
        }

        return null;
    }
}
