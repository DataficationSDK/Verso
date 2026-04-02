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
    private static string? _resolvedDllPath;

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
                _resolvedDllPath = Path.GetFullPath(dll);
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
        // 0. PYTHONNET_PYDLL env var (explicit user override, highest priority)
        var pythonNetDll = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
        if (!string.IsNullOrEmpty(pythonNetDll) && File.Exists(pythonNetDll))
            return pythonNetDll;

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

                var libDir = Path.Combine(binDir, "..", "lib");
                dll = FindPythonDllInDirectory(libDir);
                if (dll is not null) return dll;
            }
        }

        // 3. Well-known platform paths
        return DetectPlatformPythonDll();
    }

    private static string? DetectPlatformPythonDll()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return DetectMacOsPythonDll(home);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return DetectLinuxPythonDll(home);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return DetectWindowsPythonDll(home);

        return null;
    }

    private static string? DetectMacOsPythonDll(string home)
    {
        // Homebrew (Apple Silicon and Intel) and system framework
        string[] frameworkPaths =
        {
            "/opt/homebrew/Frameworks/Python.framework/Versions/Current/lib",
            "/usr/local/Frameworks/Python.framework/Versions/Current/lib",
            "/Library/Frameworks/Python.framework/Versions/Current/lib"
        };
        var dll = SearchDirectories(frameworkPaths);
        if (dll is not null) return dll;

        // uv, pyenv, conda
        return SearchVersionManagers(home, "lib") ?? SearchCondaPaths(home, "lib");
    }

    private static string? DetectLinuxPythonDll(string home)
    {
        string[] systemPaths =
        {
            "/usr/lib",
            "/usr/lib/x86_64-linux-gnu",
            "/usr/lib/aarch64-linux-gnu",
            "/usr/local/lib"
        };
        var dll = SearchDirectories(systemPaths);
        if (dll is not null) return dll;

        // uv, pyenv, conda
        return SearchVersionManagers(home, "lib") ?? SearchCondaPaths(home, "lib");
    }

    private static string? DetectWindowsPythonDll(string home)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programData = Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";

        // Standard installer locations
        string[] winBasePaths =
        {
            Path.Combine(localAppData, "Programs", "Python"),
            Path.Combine(localAppData, "Python"),
            Path.Combine(programFiles, "Python")
        };
        foreach (var basePath in winBasePaths)
        {
            var dll = FindPythonDllInDirectory(basePath);
            if (dll is not null) return dll;

            dll = SearchSubdirectories(basePath, "Python3*");
            if (dll is not null) return dll;
        }

        // Windows Store installs
        var dll2 = SearchWindowsStorePython(localAppData);
        if (dll2 is not null) return dll2;

        // uv, pyenv-win, conda
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var uvDir = Path.Combine(appData, "uv", "python");
        var pyenvDir = Path.Combine(home, ".pyenv", "pyenv-win", "versions");

        return SearchSubdirectories(uvDir, "cpython-3*")
            ?? SearchSubdirectories(pyenvDir, "3.*")
            ?? SearchCondaPaths(home, null, localAppData, programData);
    }

    /// <summary>
    /// Searches uv and pyenv install directories. On Unix, the DLL lives in a <c>lib/</c>
    /// subdirectory beneath the version folder.
    /// </summary>
    private static string? SearchVersionManagers(string home, string? dllSubdir)
    {
        // uv: ~/.local/share/uv/python/cpython-3*/
        var uvDir = Path.Combine(home, ".local", "share", "uv", "python");
        var dll = SearchSubdirectories(uvDir, "cpython-3*", dllSubdir);
        if (dll is not null) return dll;

        // pyenv: ~/.pyenv/versions/3.*/
        var pyenvDir = Path.Combine(home, ".pyenv", "versions");
        return SearchSubdirectories(pyenvDir, "3.*", dllSubdir);
    }

    /// <summary>
    /// Searches Anaconda and Miniconda install directories.
    /// </summary>
    private static string? SearchCondaPaths(string home, string? dllSubdir,
        string? localAppData = null, string? programData = null)
    {
        var paths = new List<string>
        {
            Path.Combine(home, "anaconda3"),
            Path.Combine(home, "miniconda3")
        };

        if (localAppData is not null)
        {
            paths.Add(Path.Combine(localAppData, "anaconda3"));
            paths.Add(Path.Combine(localAppData, "miniconda3"));
        }
        else
        {
            paths.Add("/opt/anaconda3");
            paths.Add("/opt/miniconda3");
        }

        if (programData is not null)
        {
            paths.Add(Path.Combine(programData, "anaconda3"));
            paths.Add(Path.Combine(programData, "miniconda3"));
        }

        foreach (var path in paths)
        {
            var searchDir = dllSubdir is not null ? Path.Combine(path, dllSubdir) : path;
            var dll = FindPythonDllInDirectory(searchDir);
            if (dll is not null) return dll;
        }

        return null;
    }

    private static string? SearchWindowsStorePython(string localAppData)
    {
        try
        {
            var packagesDir = Path.Combine(localAppData, "Packages");
            if (!Directory.Exists(packagesDir)) return null;

            var storeDirs = Directory.GetDirectories(packagesDir, "PythonSoftwareFoundation.Python.3*")
                .OrderByDescending(d => d);
            foreach (var storeDir in storeDirs)
            {
                var localPackages = Path.Combine(storeDir, "LocalCache", "local-packages");
                var dll = SearchSubdirectories(localPackages, "Python3*");
                if (dll is not null) return dll;
            }
        }
        catch
        {
            // Store package directories may have restrictive ACLs; skip silently
        }

        return null;
    }

    /// <summary>
    /// Searches subdirectories matching <paramref name="pattern"/> in descending order
    /// (highest version first). Optionally appends <paramref name="dllSubdir"/> before
    /// scanning for the Python DLL.
    /// </summary>
    private static string? SearchSubdirectories(string baseDir, string pattern, string? dllSubdir = null)
    {
        if (!Directory.Exists(baseDir)) return null;

        try
        {
            foreach (var dir in Directory.GetDirectories(baseDir, pattern).OrderByDescending(d => d))
            {
                var searchDir = dllSubdir is not null ? Path.Combine(dir, dllSubdir) : dir;
                var dll = FindPythonDllInDirectory(searchDir);
                if (dll is not null) return dll;
            }
        }
        catch
        {
            // Directory access issues; skip silently
        }

        return null;
    }

    /// <summary>
    /// Searches a flat list of directories, returning the first Python DLL found.
    /// </summary>
    private static string? SearchDirectories(string[] directories)
    {
        foreach (var dir in directories)
        {
            var dll = FindPythonDllInDirectory(dir);
            if (dll is not null) return dll;
        }

        return null;
    }

    /// <summary>
    /// Returns a Python executable that matches the loaded Python DLL version.
    /// On Windows, looks for <c>python.exe</c> alongside the DLL. On Unix,
    /// looks for <c>python3</c> in a sibling <c>bin/</c> directory.
    /// Falls back to <see cref="FindPython3OnPath"/> when the DLL path is unknown.
    /// </summary>
    internal static string? GetMatchingPythonExecutable()
    {
        if (_resolvedDllPath is not null)
        {
            var dllDir = Path.GetDirectoryName(_resolvedDllPath);
            if (dllDir is not null)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var exe = Path.Combine(dllDir, "python.exe");
                    if (File.Exists(exe)) return exe;
                }
                else
                {
                    // On Unix the DLL is typically in lib/, the executable in bin/
                    var binDir = Path.Combine(dllDir, "..", "bin");
                    var exe = Path.Combine(binDir, "python3");
                    if (File.Exists(exe)) return Path.GetFullPath(exe);

                    exe = Path.Combine(dllDir, "python3");
                    if (File.Exists(exe)) return exe;
                }
            }
        }

        return FindPython3OnPath();
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
