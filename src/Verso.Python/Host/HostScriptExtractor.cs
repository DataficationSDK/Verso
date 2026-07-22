using System.Security.Cryptography;

namespace Verso.Python.Host;

/// <summary>
/// Extracts the embedded Python host script to a per-version location under the user's Verso
/// directory so the script on disk always matches the managing assembly. Extraction is
/// idempotent: an existing, byte-identical file is left in place.
/// </summary>
internal static class HostScriptExtractor
{
    private const string ResourceName = "Verso.Python.Host.versohost.py";
    private const string ScriptFileName = "versohost.py";

    /// <summary>The per-user root under which versioned host scripts are written.</summary>
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".verso", "python", "host");

    /// <summary>
    /// Ensure the host script exists on disk and return its full path. When
    /// <paramref name="rootDirectory"/> is null, the per-user Verso host directory is used.
    /// </summary>
    public static string EnsureExtracted(string? rootDirectory = null)
    {
        var version = typeof(HostScriptExtractor).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var directory = Path.Combine(rootDirectory ?? DefaultRoot, version);
        var scriptPath = Path.Combine(directory, ScriptFileName);

        var payload = ReadEmbeddedScript();

        if (File.Exists(scriptPath) && FileMatches(scriptPath, payload))
            return scriptPath;

        Directory.CreateDirectory(directory);

        // Write to a temporary sibling then move into place so a concurrent reader never observes
        // a partially written script.
        var tempPath = Path.Combine(directory, $"{ScriptFileName}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, payload);
        try
        {
            File.Move(tempPath, scriptPath, overwrite: true);
        }
        catch
        {
            SafeDelete(tempPath);
            // Another writer may have won the race; accept its identical result.
            if (File.Exists(scriptPath) && FileMatches(scriptPath, payload))
                return scriptPath;
            throw;
        }

        return scriptPath;
    }

    private static byte[] ReadEmbeddedScript()
    {
        using var stream = typeof(HostScriptExtractor).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded host script '{ResourceName}' was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static bool FileMatches(string path, byte[] payload)
    {
        try
        {
            var existing = File.ReadAllBytes(path);
            return existing.Length == payload.Length
                && CryptographicOperations.FixedTimeEquals(existing, payload);
        }
        catch
        {
            return false;
        }
    }

    private static void SafeDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
