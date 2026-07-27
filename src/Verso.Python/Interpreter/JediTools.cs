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
/// <para>
/// The directory appears whole or not at all. Sessions start against it while an install for a
/// different notebook may be running, and one that read a half-written copy would import a
/// library it cannot use, so the install is staged beside it and moved into place in one step.
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
        // Installed beside the directory readers look in, and moved there only once it is whole.
        // Writing straight into it would publish it one file at a time: a session starting during
        // an install would find a package it can import but cannot use, and would then spend the
        // rest of its life on the fallbacks. The name is unique so two processes installing at
        // once stage separately.
        var staging = toolsPath + ".incoming-" + Path.GetRandomFileName();

        try
        {
            Directory.CreateDirectory(staging);
        }
        catch
        {
            return false;
        }

        try
        {
            var installed = useUv == UvUsage.Auto && UvTool.IsAvailable()
                ? await ProcessRunner.RunAsync(
                    UvTool.Executable,
                    new[] { "pip", "install", "--python", executable, "--target", staging, PackageName },
                    cancellationToken).ConfigureAwait(false)
                : await ProcessRunner.RunAsync(
                    executable,
                    new[]
                    {
                        "-m", "pip", "install", "--quiet", "--disable-pip-version-check", "--no-input",
                        "--target", staging, PackageName,
                    },
                    cancellationToken).ConfigureAwait(false);

            // The install reporting success is not the same as the package being there, and a
            // partial one must not be published as though it were finished.
            return installed
                && Directory.Exists(Path.Combine(staging, PackageName))
                && Publish(staging, toolsPath);
        }
        finally
        {
            // Nothing is left behind by an install that failed or lost the race to publish.
            TryDelete(staging);
        }
    }

    /// <summary>
    /// Move a finished install to where readers look for it. One step, so a session starting
    /// alongside finds either no tools or all of them. Another process getting there first is a
    /// success rather than a conflict: what matters is that the directory is now complete.
    /// </summary>
    private static bool Publish(string staging, string toolsPath)
    {
        try
        {
            var parent = Path.GetDirectoryName(toolsPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            Directory.Move(staging, toolsPath);
            return true;
        }
        catch (IOException)
        {
            return Directory.Exists(Path.Combine(toolsPath, PackageName));
        }
        catch (UnauthorizedAccessException)
        {
            return Directory.Exists(Path.Combine(toolsPath, PackageName));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort. A staging directory left behind costs disk, not correctness.
        }
    }
}
