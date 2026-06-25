namespace Verso.Extensions;

/// <summary>
/// Resolves the set of directories scanned for extension assemblies. Combines any
/// host-configured directories with a stable per-user managed directory that the
/// marketplace installs into, creating the managed directory on first use so installs
/// survive across sessions.
/// </summary>
public static class ExtensionDirectoryResolver
{
    /// <summary>
    /// Returns the per-user managed extensions directory:
    /// <c>%APPDATA%\verso\extensions</c> on Windows, <c>~/.verso/extensions</c> elsewhere.
    /// This path is stable across sessions (unlike a temp directory) so installed
    /// extensions persist.
    /// </summary>
    public static string GetDefaultManagedDir()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "verso", "extensions");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".verso", "extensions");
    }

    /// <summary>
    /// Returns the ordered, de-duplicated list of directories to scan: the configured
    /// directories followed by the managed directory. The managed directory is created if
    /// it does not yet exist and is reported via <paramref name="managedDir"/>. Non-existent
    /// configured directories are kept in the list so the caller can skip or warn per entry.
    /// </summary>
    public static IReadOnlyList<string> Resolve(IEnumerable<string?>? configuredDirs, out string managedDir)
    {
        managedDir = GetDefaultManagedDir();
        try
        {
            Directory.CreateDirectory(managedDir);
        }
        catch
        {
            // If the managed directory can't be created, scanning simply skips it later.
        }

        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            var full = dir.Trim();
            if (seen.Add(full))
                ordered.Add(full);
        }

        if (configuredDirs is not null)
        {
            foreach (var dir in configuredDirs)
                Add(dir);
        }

        Add(managedDir);
        return ordered;
    }
}
