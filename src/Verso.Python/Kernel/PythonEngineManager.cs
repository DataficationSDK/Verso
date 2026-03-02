using System.Diagnostics;
using System.Runtime.InteropServices;
using Python.Runtime;

namespace Verso.Python.Kernel;

/// <summary>
/// Manages process-wide initialization of the Python engine. The engine can only be
/// initialized once per process; this class enforces that with a double-check lock.
/// </summary>
internal static class PythonEngineManager
{
    private static readonly object Lock = new();
    private static bool _initialized;

    /// <summary>
    /// Ensures the Python engine is initialized exactly once. Safe to call from multiple threads.
    /// </summary>
    /// <param name="pythonDll">
    /// Explicit path or library name for the Python shared library.
    /// When <c>null</c>, auto-detection is attempted.
    /// </param>
    public static void EnsureInitialized(string? pythonDll = null)
    {
        if (_initialized) return;

        lock (Lock)
        {
            if (_initialized) return;

            var dll = pythonDll ?? DetectPythonDll();
            if (dll is not null)
            {
                Runtime.PythonDLL = dll;
            }

            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();

            _initialized = true;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the Python engine has been initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Auto-detects the Python shared library path by checking environment variables,
    /// the system PATH, and well-known platform locations.
    /// </summary>
    private static string? DetectPythonDll()
    {
        // 1. PYTHONHOME env var
        var pythonHome = Environment.GetEnvironmentVariable("PYTHONHOME");
        if (!string.IsNullOrEmpty(pythonHome))
        {
            var dll = FindPythonDllInDirectory(pythonHome);
            if (dll is not null) return dll;
        }

        // 2. python3 on PATH: resolve symlinks, find DLL in bin/ or ../lib/
        var python3Path = FindPython3OnPath();
        if (python3Path is not null)
        {
            var binDir = Path.GetDirectoryName(python3Path);
            if (binDir is not null)
            {
                var dll = FindPythonDllInDirectory(binDir);
                if (dll is not null) return dll;

                // Check ../lib/ relative to bin
                var libDir = Path.Combine(binDir, "..", "lib");
                dll = FindPythonDllInDirectory(libDir);
                if (dll is not null) return dll;
            }
        }

        // 3. Well-known platform paths
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Homebrew (Apple Silicon and Intel)
            string[] macPaths =
            {
                "/opt/homebrew/Frameworks/Python.framework/Versions/Current/lib",
                "/usr/local/Frameworks/Python.framework/Versions/Current/lib",
                "/Library/Frameworks/Python.framework/Versions/Current/lib"
            };
            foreach (var path in macPaths)
            {
                var dll = FindPythonDllInDirectory(path);
                if (dll is not null) return dll;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string[] linuxPaths =
            {
                "/usr/lib",
                "/usr/lib/x86_64-linux-gnu",
                "/usr/lib/aarch64-linux-gnu",
                "/usr/local/lib"
            };
            foreach (var path in linuxPaths)
            {
                var dll = FindPythonDllInDirectory(path);
                if (dll is not null) return dll;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Common Windows install locations
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] winPaths =
            {
                Path.Combine(localAppData, "Programs", "Python"),  // Standard MSI installer
                Path.Combine(localAppData, "Python"),               // python.org / nuget layout
                Path.Combine(programFiles, "Python")
            };
            foreach (var basePath in winPaths)
            {
                if (!Directory.Exists(basePath)) continue;

                // Check the directory itself (e.g. %LOCALAPPDATA%\Python has python3*.dll)
                var dll = FindPythonDllInDirectory(basePath);
                if (dll is not null) return dll;

                // Check Python3* subdirectories (e.g. Programs\Python\Python312)
                foreach (var dir in Directory.GetDirectories(basePath, "Python3*").OrderByDescending(d => d))
                {
                    dll = FindPythonDllInDirectory(dir);
                    if (dll is not null) return dll;
                }
            }
        }

        return null;
    }

    internal static string? FindPython3OnPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: try python3.exe first, then fall back to python.exe
            // (standard Windows installer only provides python.exe)
            return WhereFirst("python3.exe") ?? WhereFirst("python.exe");
        }

        try
        {
            var psi = new ProcessStartInfo("which", "python3")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (process.ExitCode != 0 || string.IsNullOrEmpty(output)) return null;

            var firstLine = output.Split('\n')[0].Trim();

            // Resolve symlinks on Unix
            if (File.Exists(firstLine))
            {
                var resolved = ResolveSymlink(firstLine);
                if (resolved is not null) return resolved;
            }

            return firstLine;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Runs <c>where &lt;fileName&gt;</c> on Windows and returns the first result that
    /// is not the Windows Store app stub (<c>WindowsApps</c>).
    /// </summary>
    private static string? WhereFirst(string fileName)
    {
        try
        {
            var psi = new ProcessStartInfo("where", fileName)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (process.ExitCode != 0 || string.IsNullOrEmpty(output)) return null;

            // where.exe may return multiple lines; skip the Windows Store stub
            foreach (var line in output.Split('\n'))
            {
                var candidate = line.Trim();
                if (!string.IsNullOrEmpty(candidate) && !candidate.Contains("WindowsApps"))
                    return candidate;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveSymlink(string path)
    {
        try
        {
            var psi = new ProcessStartInfo("readlink", $"-f \"{path}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return path;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : path;
        }
        catch
        {
            return path;
        }
    }

    private static string? FindPythonDllInDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        try
        {
            // Look for libpython3.X.so, libpython3.X.dylib, python3X.dll
            var patterns = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { "python3*.dll" }
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? new[] { "libpython3*.dylib" }
                    : new[] { "libpython3*.so", "libpython3*.so.*" };

            foreach (var pattern in patterns)
            {
                var files = Directory.GetFiles(directory, pattern)
                    .Where(f => !f.Contains("config") && !f.Contains("test"))
                    .OrderByDescending(f => f)
                    .ToArray();

                if (files.Length > 0) return files[0];
            }
        }
        catch
        {
            // Directory access issues; skip silently
        }

        return null;
    }
}
