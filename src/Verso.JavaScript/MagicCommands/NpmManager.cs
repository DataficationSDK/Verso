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
    /// Installs npm packages into the Verso node directory and reports what arrived.
    /// </summary>
    /// <param name="packages">The specifiers as the cell wrote them.</param>
    /// <param name="requested">The package names those specifiers name, used to tell what was
    /// asked for from what came with it.</param>
    /// <param name="context">Where the report goes.</param>
    /// <param name="detail">Whether to name every package rather than count the incidental ones.</param>
    /// <param name="ct">Cancels the install.</param>
    public static async Task<bool> InstallAsync(
        string packages,
        IReadOnlyList<string> requested,
        IVersoContext context,
        bool detail,
        CancellationToken ct)
    {
        var npmExe = FindNpm();
        if (npmExe is null)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "npm not found on PATH.", IsError: true, ErrorName: "NpmError"));
            return false;
        }

        await EnsureInitializedAsync(ct);

        // Asked for its report as JSON. npm's prose counts packages without naming them, so the
        // version that arrived cannot be said at all without this.
        var args = $"install --prefix \"{VersoNodeDir}\" --save --json {packages}";
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

        var report = NpmReport.Parse(stdout, stderr);

        if (proc.ExitCode != 0)
        {
            await ReportFailureAsync(report, stderr, context);
            return false;
        }

        // An npm too old to report as JSON is relayed as it stands rather than summarized into a
        // claim about a run that was never read.
        if (!report.Understood)
        {
            if (!string.IsNullOrWhiteSpace(stdout))
                await context.WriteOutputAsync(new CellOutput("text/plain", stdout.Trim()));
            return true;
        }

        foreach (var line in report.Summarize(requested, detail))
            await context.WriteOutputAsync(new CellOutput("text/plain", line));

        return true;
    }

    /// <summary>
    /// Reports a failure in full whatever the display setting is. npm's warning stream carries
    /// the readable account of what went wrong, and it is the only account there is.
    /// </summary>
    private static async Task ReportFailureAsync(
        NpmReport report, string standardError, IVersoContext context)
    {
        var explanation = string.IsNullOrWhiteSpace(standardError)
            ? report.Error
            : standardError.Trim();

        if (!string.IsNullOrWhiteSpace(explanation))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", explanation!, IsError: true, ErrorName: "NpmError"));
            return;
        }

        await context.WriteOutputAsync(new CellOutput(
            "text/plain", "The npm install failed.", IsError: true, ErrorName: "NpmError"));
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
    /// The package a specifier names, dropping any version that follows it. A scoped package
    /// begins with an <c>@</c> of its own, so the version separator is the next one along rather
    /// than the first.
    /// </summary>
    public static string PackageName(string specifier)
    {
        var spec = (specifier ?? string.Empty).Trim();
        if (spec.Length == 0)
            return string.Empty;

        var start = spec.StartsWith('@') ? 1 : 0;
        var separator = spec.IndexOf('@', start);
        return separator < 0 ? spec : spec[..separator];
    }

    /// <summary>
    /// Quick check whether a package directory exists in node_modules.
    /// </summary>
    public static bool IsPackageInstalled(string packageName)
    {
        // An empty name would otherwise test node_modules itself, which exists, and report that
        // anything at all is already there.
        if (string.IsNullOrWhiteSpace(packageName))
            return false;

        var dir = Path.Combine(NodeModulesPath, packageName);
        return Directory.Exists(dir);
    }

    /// <summary>
    /// Reads the installed version of a package from its package.json in node_modules.
    /// Returns null when the package is not installed or the version cannot be read.
    /// </summary>
    public static string? GetInstalledPackageVersion(string packageName)
    {
        try
        {
            var packageJson = Path.Combine(NodeModulesPath, packageName, "package.json");
            if (!File.Exists(packageJson)) return null;

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(packageJson));
            return doc.RootElement.TryGetProperty("version", out var version)
                ? version.GetString()
                : null;
        }
        catch
        {
            return null;
        }
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
