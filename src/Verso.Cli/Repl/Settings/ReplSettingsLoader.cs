using System.Text.Json;

namespace Verso.Cli.Repl.Settings;

/// <summary>
/// Loads <see cref="ReplSettings"/> from the user config directory. Invalid or
/// missing files are treated as empty — settings never cause the REPL to refuse
/// to start.
/// </summary>
public static class ReplSettingsLoader
{
    /// <summary>
    /// Returns the path to the user-level config file (exists or not).
    /// </summary>
    public static string GetUserConfigPath()
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
        return Path.Combine(root, "verso", "repl.json");
    }

    /// <summary>
    /// Reads user settings from disk. Returns defaults when the file is missing
    /// or malformed.
    /// </summary>
    public static ReplSettings Load()
    {
        var settings = new ReplSettings();
        var path = GetUserConfigPath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                ApplyJson(settings, json);
            }
            catch
            {
                // Malformed user settings must never block the REPL.
            }
        }
        return settings;
    }

    /// <summary>
    /// Applies a JSON string to the given settings object. Useful for tests and
    /// potential future notebook-scoped overrides.
    /// </summary>
    public static void ApplyJsonOverride(ReplSettings settings, string json)
    {
        try { ApplyJson(settings, json); } catch { /* ignore malformed */ }
    }

    private static void ApplyJson(ReplSettings target, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;

        if (TryGet(root, "prompt", out var p) && p.ValueKind == JsonValueKind.String)
            target.Prompt = p.GetString() ?? target.Prompt;

        if (TryGet(root, "confirmOnExit", out var c) && c.ValueKind is JsonValueKind.True or JsonValueKind.False)
            target.ConfirmOnExit = c.GetBoolean();

        if (TryGet(root, "preview", out var preview) && preview.ValueKind == JsonValueKind.Object)
        {
            if (TryGet(preview, "rows", out var rows) && rows.TryGetInt32(out var rv)) target.Preview.Rows = rv;
            if (TryGet(preview, "lines", out var lines) && lines.TryGetInt32(out var lv)) target.Preview.Lines = lv;
            if (TryGet(preview, "elapsedThresholdMs", out var el) && el.TryGetInt32(out var ev))
                target.Preview.ElapsedThresholdMs = ev;
        }
    }

    private static bool TryGet(JsonElement el, string name, out JsonElement value)
    {
        // JSON keys are case-sensitive; accept either casing to be forgiving of hand-edited files.
        foreach (var prop in el.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
