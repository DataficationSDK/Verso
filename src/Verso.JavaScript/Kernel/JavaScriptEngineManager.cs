using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Verso.JavaScript.Kernel;

/// <summary>
/// Detects whether Node.js is available on the system. The result is cached process-wide.
/// </summary>
internal static class JavaScriptEngineManager
{
    private static readonly Lazy<NodeDetectionResult?> _nodeResult =
        new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Returns the detected Node.js installation, or null if not found.
    /// </summary>
    public static NodeDetectionResult? NodeInfo => _nodeResult.Value;

    /// <summary>
    /// True if Node.js was found on this system.
    /// </summary>
    public static bool NodeAvailable => _nodeResult.Value is not null;

    /// <summary>
    /// The path to the Node.js executable, or null if not found.
    /// </summary>
    public static string? NodeExecutablePath => _nodeResult.Value?.ExecutablePath;

    private static NodeDetectionResult? Detect()
    {
        var path = FindNodeOnPath() ?? FindNodeAtWellKnownLocations();
        if (path is null) return null;

        var version = GetNodeVersion(path);
        return version is not null ? new NodeDetectionResult(path, version) : null;
    }

    private static string? FindNodeOnPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WhereFirst("node.exe");

        try
        {
            var psi = new ProcessStartInfo("which", "node")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return null;

            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (firstLine is null || !File.Exists(firstLine)) return null;

            // Resolve symlinks
            var resolved = ResolveSymlink(firstLine);
            return resolved;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindNodeAtWellKnownLocations()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            candidates.AddRange([
                "/opt/homebrew/bin/node",
                "/usr/local/bin/node",
            ]);
            AddNvmPaths(Path.Combine(home, ".nvm", "versions", "node"), candidates);
            candidates.Add(Path.Combine(home, ".volta", "bin", "node"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            candidates.AddRange([
                "/usr/bin/node",
                "/usr/local/bin/node",
                "/snap/bin/node",
            ]);
            AddNvmPaths(Path.Combine(home, ".nvm", "versions", "node"), candidates);
            candidates.Add(Path.Combine(home, ".volta", "bin", "node"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(programFiles, "nodejs", "node.exe"));

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            AddNvmPaths(Path.Combine(appData, "nvm"), candidates, "node.exe");

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            AddFnmPaths(Path.Combine(localAppData, "fnm", "node-versions"), candidates);
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void AddNvmPaths(string nvmDir, List<string> candidates, string binary = "node")
    {
        if (!Directory.Exists(nvmDir)) return;
        try
        {
            var versions = Directory.GetDirectories(nvmDir)
                .OrderByDescending(d => d)
                .ToList();

            foreach (var versionDir in versions)
            {
                var binPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(versionDir, binary)
                    : Path.Combine(versionDir, "bin", binary);
                candidates.Add(binPath);
            }
        }
        catch { }
    }

    private static void AddFnmPaths(string fnmDir, List<string> candidates)
    {
        if (!Directory.Exists(fnmDir)) return;
        try
        {
            var versions = Directory.GetDirectories(fnmDir)
                .OrderByDescending(d => d)
                .ToList();

            foreach (var versionDir in versions)
            {
                var installDir = Path.Combine(versionDir, "installation");
                candidates.Add(Path.Combine(installDir, "node.exe"));
            }
        }
        catch { }
    }

    private static string? GetNodeVersion(string nodePath)
    {
        try
        {
            var psi = new ProcessStartInfo(nodePath, "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);

            return proc.ExitCode == 0 && output.StartsWith('v') ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? WhereFirst(string fileName)
    {
        try
        {
            var psi = new ProcessStartInfo("where", fileName)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);

            if (proc.ExitCode != 0) return null;

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveSymlink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.LinkTarget is not null ? Path.GetFullPath(info.LinkTarget, Path.GetDirectoryName(path)!) : path;
        }
        catch
        {
            return path;
        }
    }
}

internal sealed record NodeDetectionResult(string ExecutablePath, string Version);
