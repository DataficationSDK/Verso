using System.Collections.Concurrent;
using Verso.Python.Kernel;

namespace Verso.Python.Interpreter;

/// <summary>
/// Provisions the analysis library the Python host uses for completions, hover, and diagnostics.
/// It is installed into a directory of its own rather than into the interpreter's environment,
/// so a notebook's environment holds only what the notebook asked for, and the host puts that
/// directory at the end of its import path where it cannot shadow a user's own packages.
/// <para>
/// The directory is keyed by Python minor version because the library is pure Python and shared
/// by every interpreter of that version. Provisioning is idempotent, runs at most once per
/// directory per process, and fails silently: an offline machine keeps the standard-library
/// fallbacks rather than an error the user cannot act on.
/// </para>
/// </summary>
internal static class JediTools
{
    /// <summary>The package to install, and the directory name it occupies once installed.</summary>
    private const string PackageName = "jedi";

    private static readonly string DefaultRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".verso", "python", "tools");

    /// <summary>
    /// Provisioning runs in flight of a notebook opening, and several notebooks can open at once
    /// against the same interpreter. Sharing the task keeps that to a single install.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Task<bool>> Pending =
        new(StringComparer.Ordinal);

    /// <summary>The directory holding the tools for a given Python version.</summary>
    public static string ComputeToolsPath(Version version, string? root = null)
        => Path.Combine(root ?? DefaultRoot, $"{version.Major}.{version.Minor}");

    /// <summary>Whether the tools are already present for a given Python version.</summary>
    public static bool IsProvisioned(Version version, string? root = null)
        => Directory.Exists(Path.Combine(ComputeToolsPath(version, root), PackageName));

    /// <summary>
    /// Ensure the tools are installed for the interpreter's version, returning whether they are
    /// available afterwards. Never throws.
    /// </summary>
    public static async Task<bool> EnsureProvisionedAsync(
        string executable, Version version, UvUsage useUv, CancellationToken cancellationToken,
        string? root = null)
    {
        var toolsPath = ComputeToolsPath(version, root);
        if (Directory.Exists(Path.Combine(toolsPath, PackageName)))
            return true;

        // GetOrAdd can run the factory more than once under contention, so the task it builds is
        // deliberately cheap to create and idempotent to run.
        var install = Pending.GetOrAdd(
            toolsPath,
            static (path, state) => InstallAsync(path, state.Executable, state.UseUv, state.Token),
            (Executable: executable, UseUv: useUv, Token: cancellationToken));

        bool provisioned;
        try
        {
            provisioned = await install.ConfigureAwait(false);
        }
        catch
        {
            provisioned = false;
        }

        // A failed attempt is forgotten so a later session can try again, for instance once the
        // machine is back online. Keyed on the task itself, so an attempt already under way is
        // not dropped along with it.
        if (!provisioned)
            Pending.TryRemove(new KeyValuePair<string, Task<bool>>(toolsPath, install));

        return provisioned;
    }

    /// <summary>Forgets the in-process record of which directories have been provisioned. For tests.</summary>
    internal static void ResetPending() => Pending.Clear();

    private static async Task<bool> InstallAsync(
        string toolsPath, string executable, UvUsage useUv, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(toolsPath);
        }
        catch
        {
            return false;
        }

        var installed = useUv == UvUsage.Auto && UvTool.IsAvailable()
            ? await ProcessRunner.RunAsync(
                UvTool.Executable,
                new[] { "pip", "install", "--python", executable, "--target", toolsPath, PackageName },
                cancellationToken).ConfigureAwait(false)
            : await ProcessRunner.RunAsync(
                executable,
                new[]
                {
                    "-m", "pip", "install", "--quiet", "--disable-pip-version-check", "--no-input",
                    "--target", toolsPath, PackageName,
                },
                cancellationToken).ConfigureAwait(false);

        // The install reporting success is not the same as the package being importable, and a
        // partial install would leave the host retrying a directory that will never serve it.
        return installed && Directory.Exists(Path.Combine(toolsPath, PackageName));
    }
}
