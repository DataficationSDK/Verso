using System.Security.Cryptography;

namespace Verso.Python.Host;

/// <summary>
/// Extracts the embedded Python host script and its supporting modules to a per-version
/// location under the user's Verso directory, so what runs on disk always matches the managing
/// assembly. Extraction is idempotent: an existing, byte-identical file is left in place.
/// </summary>
internal static class HostScriptExtractor
{
    private const string ResourcePrefix = "Verso.Python.Host.";
    private const string ScriptFileName = "versohost.py";

    /// <summary>
    /// The entry script first, then the modules it imports. All of them are written before the
    /// path is handed out, because the script imports its siblings at load time.
    /// </summary>
    private static readonly string[] ScriptFileNames =
    {
        ScriptFileName,
        "_versohost_bind.py",
        "_versohost_comm.py",
        "_versohost_display.py",
        "_versohost_intel.py",
        "_versohost_scan.py",
        "_versohost_vars.py",
        "_versohost_wiretypes.py",
    };

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

        foreach (var fileName in ScriptFileNames)
            EnsureFileExtracted(directory, fileName);

        return Path.Combine(directory, ScriptFileName);
    }

    private static void EnsureFileExtracted(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        var payload = ReadEmbeddedScript(fileName);

        if (File.Exists(path) && FileMatches(path, payload))
            return;

        Directory.CreateDirectory(directory);

        // Write to a temporary sibling then move into place so a concurrent reader never observes
        // a partially written script.
        var tempPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, payload);
        try
        {
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            SafeDelete(tempPath);
            // Another writer may have won the race; accept its identical result.
            if (File.Exists(path) && FileMatches(path, payload))
                return;
            throw;
        }
    }

    private static byte[] ReadEmbeddedScript(string fileName)
    {
        var resourceName = ResourcePrefix + fileName;
        using var stream = typeof(HostScriptExtractor).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded host script '{resourceName}' was not found.");
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
