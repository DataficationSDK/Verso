using System.Diagnostics;
using Verso.Abstractions;

namespace Verso.JavaScript.MagicCommands;

/// <summary>
/// Manages a per-user npm directory at <c>~/.verso/node/</c> for JavaScript package installs.
/// </summary>
internal static class NpmManager
{
    public const string NodePathStoreKey = "__verso_npm_node_path";

    private static string VersoNodeDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".verso", "node");

    public static string NodeModulesPath => Path.Combine(VersoNodeDir, "node_modules");

    /// <summary>
    /// Ensures the ~/.verso/node/ directory exists with a minimal package.json.
    /// </summary>
    public static async Task EnsureInitializedAsync(CancellationToken ct)
    {
        var dir = VersoNodeDir;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var packageJson = Path.Combine(dir, "package.json");
        if (!File.Exists(packageJson))
        {
            await File.WriteAllTextAsync(packageJson,
                """{"name":"verso-npm","version":"1.0.0","private":true}""", ct);
        }
    }

    /// <summary>
    /// Installs npm packages into the Verso node directory.
    /// </summary>
    public static async Task<bool> InstallAsync(
        string packages, IVersoContext context, CancellationToken ct)
    {
        var npmExe = FindNpm();
        if (npmExe is null)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "npm not found on PATH.", IsError: true, ErrorName: "NpmError"));
            return false;
        }

        await EnsureInitializedAsync(ct);

        var args = $"install --prefix \"{VersoNodeDir}\" --save {packages}";
        var psi = new ProcessStartInfo(npmExe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = VersoNodeDir,
        };

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "Failed to start npm process.", IsError: true, ErrorName: "NpmError"));
            return false;
        }

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (!string.IsNullOrWhiteSpace(stdout))
            await context.WriteOutputAsync(new CellOutput("text/plain", stdout.Trim()));

        if (proc.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(stderr))
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain", stderr.Trim(), IsError: true, ErrorName: "NpmError"));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Installs npm packages silently, suppressing all output on success.
    /// Used for automatic background installs (e.g. TypeScript compiler).
    /// </summary>
    public static async Task<bool> InstallSilentAsync(string packages, CancellationToken ct)
    {
        var npmExe = FindNpm();
        if (npmExe is null) return false;

        await EnsureInitializedAsync(ct);

        var args = $"install --prefix \"{VersoNodeDir}\" --save {packages}";
        var psi = new ProcessStartInfo(npmExe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = VersoNodeDir,
        };

        using var proc = Process.Start(psi);
        if (proc is null) return false;

        await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return proc.ExitCode == 0;
    }

    /// <summary>
    /// Quick check whether a package directory exists in node_modules.
    /// </summary>
    public static bool IsPackageInstalled(string packageName)
    {
        var dir = Path.Combine(NodeModulesPath, packageName);
        return Directory.Exists(dir);
    }

    private static string? FindNpm()
    {
        try
        {
            var cmd = OperatingSystem.IsWindows() ? "where" : "which";
            var arg = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";

            var psi = new ProcessStartInfo(cmd, arg)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);

            return proc.ExitCode == 0
                ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
