using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verso.Extensions;

/// <summary>
/// Persists per-package extension trust decisions across sessions in a small JSON file
/// under the user config directory. The first time a notebook needs an extension the
/// hosting layer prompts for consent; once approved the decision is remembered here so
/// later opens load it without re-prompting. Trust is keyed by package id and records the
/// approved version, so a request for a different version re-prompts.
/// </summary>
public sealed class ExtensionTrustStore
{
    private readonly Dictionary<string, TrustEntry> _entries;
    private readonly string _path;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private ExtensionTrustStore(string path, Dictionary<string, TrustEntry> entries)
    {
        _path = path;
        _entries = entries;
    }

    /// <summary>
    /// Returns the path to the trust file (whether or not it exists). Mirrors the user
    /// config directory layout used elsewhere: <c>%LOCALAPPDATA%\verso\trust.json</c> on
    /// Windows, <c>$XDG_CONFIG_HOME/verso/trust.json</c> (or <c>~/.config/...</c>) elsewhere.
    /// </summary>
    public static string GetTrustStorePath()
    {
        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            root = !string.IsNullOrEmpty(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        return Path.Combine(root, "verso", "trust.json");
    }

    /// <summary>
    /// Loads the trust store from disk. A missing or malformed file yields an empty store;
    /// trust persistence must never prevent a notebook from opening.
    /// </summary>
    public static ExtensionTrustStore Load() => Load(GetTrustStorePath());

    /// <summary>Loads the trust store from a specific path (used by tests).</summary>
    public static ExtensionTrustStore Load(string path)
    {
        var entries = new Dictionary<string, TrustEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, TrustEntry>>(json, SerializerOptions);
                if (parsed is not null)
                {
                    foreach (var (key, value) in parsed)
                        entries[key] = value;
                }
            }
        }
        catch
        {
            // A malformed trust file must never block opening a notebook.
        }
        return new ExtensionTrustStore(path, entries);
    }

    /// <summary>
    /// Whether the given package is approved for the requested version. Approval is pinned to
    /// the exact version the user consented to, so a request for a different version re-prompts.
    /// A null on either side means "unspecified version" and matches only the other side also
    /// being null; it does not blanket-approve every version. This keeps a one-time "latest"
    /// approval from silently trusting a later, possibly malicious, version.
    /// </summary>
    public bool IsApproved(string packageId, string? version)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return false;
        if (!_entries.TryGetValue(packageId, out var entry))
            return false;
        if (entry.ApprovedVersion is null || version is null)
            return entry.ApprovedVersion is null && version is null;
        return string.Equals(entry.ApprovedVersion, version, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Records approval for a package at the given version (null = latest).</summary>
    public void Approve(string packageId, string? version)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return;
        _entries[packageId] = new TrustEntry
        {
            ApprovedVersion = version,
            ApprovedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>Removes any approval for a package.</summary>
    public void Revoke(string packageId)
    {
        if (!string.IsNullOrWhiteSpace(packageId))
            _entries.Remove(packageId);
    }

    /// <summary>Writes the store to disk. Best effort; failures are swallowed.</summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_entries, SerializerOptions);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Best effort — an unwritable trust file simply means re-prompting next time.
        }
    }

    /// <summary>One stored trust decision.</summary>
    public sealed class TrustEntry
    {
        /// <summary>The approved version, or null when the latest version was approved.</summary>
        public string? ApprovedVersion { get; set; }

        /// <summary>When the approval was granted.</summary>
        public DateTimeOffset ApprovedAtUtc { get; set; }
    }
}
