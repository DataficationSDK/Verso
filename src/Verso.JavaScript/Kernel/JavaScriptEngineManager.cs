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

    /// <summary>
    /// Environment variable that overrides Node.js discovery with an explicit executable
    /// path. Lets users of any host (the <c>verso serve</c> CLI, Verso.Blazor, ...) point
    /// Verso at a specific Node.js when auto-detection cannot find it, for example with a
    /// version manager whose install location is not on the well-known list.
    /// </summary>
    internal const string NodePathEnvVar = "VERSO_NODE_PATH";

    private static NodeDetectionResult? Detect()
    {
        // Try each candidate in priority order and accept the first that runs. Validating
        // every candidate (rather than committing to the first one found) means a broken
        // entry on PATH falls through to the well-known locations instead of aborting
        // discovery outright.
        foreach (var candidate in EnumerateCandidates())
        {
            var version = GetNodeVersion(candidate);
            if (version is not null)
                return new NodeDetectionResult(candidate, version);
        }

        return null;
    }

    internal static IEnumerable<string> EnumerateCandidates()
    {
        // 1. Explicit override via environment variable.
        var envPath = Environment.GetEnvironmentVariable(NodePathEnvVar);
        if (!string.IsNullOrWhiteSpace(envPath))
            yield return envPath.Trim();

        // 2. node as found on PATH.
        var onPath = FindNodeOnPath();
        if (onPath is not null)
            yield return onPath;

        // 3. Well-known install locations for common version and package managers.
        foreach (var wellKnown in EnumerateWellKnownLocations())
            yield return wellKnown;
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

    private static IEnumerable<string> EnumerateWellKnownLocations()
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

        return candidates.Where(File.Exists);
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
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            // Drain stderr so a candidate that rejects direct invocation (e.g. a shim
            // dispatcher) cannot leak its message to the host's terminal.
            _ = proc.StandardError.ReadToEnd();
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

    /// <summary>
    /// Resolves a one-hop symlink to its target, but only when the target keeps the same
    /// file name. Shim-based version managers (notably Volta) expose <c>node</c> as a link
    /// to a single dispatcher binary, for example <c>volta-shim</c>, that decides which
    /// tool to run by inspecting how it was invoked (argv[0]). Following such a link and
    /// launching the dispatcher directly makes it refuse to run ("'volta-shim' should not
    /// be called directly"), so when the target name differs we keep the original link
    /// path and let the OS follow it at launch, which preserves the <c>node</c> invocation
    /// name. Links that still resolve to a file named <c>node</c> (Homebrew's Cellar links,
    /// fnm's per-shell links) are resolved as before to obtain a stable path.
    /// </summary>
    internal static string ResolveSymlink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is null)
                return path;

            var resolved = Path.GetFullPath(info.LinkTarget, Path.GetDirectoryName(path)!);

            var originalName = Path.GetFileNameWithoutExtension(path);
            var resolvedName = Path.GetFileNameWithoutExtension(resolved);
            return string.Equals(originalName, resolvedName, StringComparison.OrdinalIgnoreCase)
                ? resolved
                : path;
        }
        catch
        {
            return path;
        }
    }
}

internal sealed record NodeDetectionResult(string ExecutablePath, string Version);
