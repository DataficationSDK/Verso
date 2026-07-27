using System.Runtime.InteropServices;

namespace Verso.Python.Interpreter;

/// <summary>
/// Enumerates candidate Python <em>executables</em> in well-known installation locations, as a last
/// resort when no environment variable, workspace environment, or search-path interpreter applies.
/// The locations mirror the interpreter families Verso supports (framework and Homebrew builds on
/// macOS, distro paths on Linux, standard installer and Store builds on Windows, plus uv, pyenv, and
/// conda everywhere). Candidates are yielded highest-version-first and are pre-filtered to paths that
/// exist; the caller validates each in turn and keeps the first that qualifies.
/// </summary>
internal static class WellKnownInterpreterPaths
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string ExecutableLeaf => IsWindows ? "python.exe" : "python3";

    /// <summary>Yields existing candidate executables in precedence order.</summary>
    public static IEnumerable<string> EnumerateCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            foreach (var path in MacOsCandidates(home)) yield return path;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            foreach (var path in LinuxCandidates(home)) yield return path;
        }
        else if (IsWindows)
        {
            foreach (var path in WindowsCandidates(home)) yield return path;
        }

        foreach (var path in VersionManagerCandidates(home)) yield return path;
        foreach (var path in CondaCandidates(home)) yield return path;
    }

    private static IEnumerable<string> MacOsCandidates(string home)
    {
        string[] frameworkBinDirs =
        {
            "/opt/homebrew/Frameworks/Python.framework/Versions/Current/bin",
            "/usr/local/Frameworks/Python.framework/Versions/Current/bin",
            "/Library/Frameworks/Python.framework/Versions/Current/bin",
            "/opt/homebrew/bin",
            "/usr/local/bin",
        };
        foreach (var dir in frameworkBinDirs)
            foreach (var exe in ExistingExecutablesIn(dir))
                yield return exe;
    }

    private static IEnumerable<string> LinuxCandidates(string home)
    {
        string[] binDirs =
        {
            "/usr/bin",
            "/usr/local/bin",
            "/bin",
        };
        foreach (var dir in binDirs)
            foreach (var exe in ExistingExecutablesIn(dir))
                yield return exe;
    }

    private static IEnumerable<string> WindowsCandidates(string home)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] baseDirs =
        {
            Path.Combine(localAppData, "Programs", "Python"),
            Path.Combine(localAppData, "Python"),
            Path.Combine(programFiles, "Python"),
        };

        foreach (var baseDir in baseDirs)
            foreach (var exe in VersionedExecutablesUnder(baseDir, "Python3*", ""))
                yield return exe;

        // Python's own install manager keeps the runtimes it downloads in a layout of its own, named
        // for the build rather than the version (for example pythoncore-3.14-64), so the patterns
        // above do not reach them. An interpreter installed that way is often the only one present.
        foreach (var exe in VersionedExecutablesUnder(Path.Combine(localAppData, "Python"), "pythoncore-*", ""))
            yield return exe;

        // Windows Store builds.
        var storePackages = Path.Combine(localAppData, "Packages");
        foreach (var storeDir in DirectoriesMatching(storePackages, "PythonSoftwareFoundation.Python.3*"))
        {
            var localPackages = Path.Combine(storeDir, "LocalCache", "local-packages");
            foreach (var exe in VersionedExecutablesUnder(localPackages, "Python3*", ""))
                yield return exe;
        }
    }

    private static IEnumerable<string> VersionManagerCandidates(string home)
    {
        // uv-managed interpreters.
        var uvUnix = Path.Combine(home, ".local", "share", "uv", "python");
        var uvWindows = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "uv", "python");
        foreach (var exe in VersionedExecutablesUnder(uvUnix, "cpython-3*", IsWindows ? "" : "bin"))
            yield return exe;
        foreach (var exe in VersionedExecutablesUnder(uvWindows, "cpython-3*", ""))
            yield return exe;

        // pyenv.
        var pyenvUnix = Path.Combine(home, ".pyenv", "versions");
        var pyenvWindows = Path.Combine(home, ".pyenv", "pyenv-win", "versions");
        foreach (var exe in VersionedExecutablesUnder(pyenvUnix, "3.*", IsWindows ? "" : "bin"))
            yield return exe;
        foreach (var exe in VersionedExecutablesUnder(pyenvWindows, "3.*", ""))
            yield return exe;
    }

    private static IEnumerable<string> CondaCandidates(string home)
    {
        var roots = new List<string>
        {
            Path.Combine(home, "anaconda3"),
            Path.Combine(home, "miniconda3"),
        };

        if (IsWindows)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            roots.Add(Path.Combine(localAppData, "anaconda3"));
            roots.Add(Path.Combine(localAppData, "miniconda3"));
        }
        else
        {
            roots.Add("/opt/anaconda3");
            roots.Add("/opt/miniconda3");
        }

        foreach (var root in roots)
        {
            var exe = IsWindows
                ? Path.Combine(root, "python.exe")
                : Path.Combine(root, "bin", "python3");
            if (File.Exists(exe))
                yield return exe;
        }
    }

    /// <summary>Yields <c>python3</c>/<c>python</c> and versioned variants that exist directly in a bin directory.</summary>
    private static IEnumerable<string> ExistingExecutablesIn(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        string[] leaves = IsWindows
            ? new[] { "python.exe" }
            : new[] { "python3", "python" };

        foreach (var leaf in leaves)
        {
            var path = Path.Combine(directory, leaf);
            if (File.Exists(path))
                yield return path;
        }
    }

    /// <summary>
    /// Yields executables found under version-named subdirectories of <paramref name="baseDir"/>,
    /// highest version first. <paramref name="relativeBin"/> is the directory (relative to each version
    /// folder) that holds the executable, empty when the executable sits at the version-folder root.
    /// </summary>
    private static IEnumerable<string> VersionedExecutablesUnder(string baseDir, string pattern, string relativeBin)
    {
        foreach (var dir in DirectoriesMatching(baseDir, pattern))
        {
            var exeDir = string.IsNullOrEmpty(relativeBin) ? dir : Path.Combine(dir, relativeBin);
            var exe = Path.Combine(exeDir, ExecutableLeaf);
            if (File.Exists(exe))
                yield return exe;
        }
    }

    private static IEnumerable<string> DirectoriesMatching(string baseDir, string pattern)
    {
        if (!Directory.Exists(baseDir))
            return Array.Empty<string>();

        try
        {
            return Directory.GetDirectories(baseDir, pattern).OrderByDescending(d => d, StringComparer.Ordinal);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
