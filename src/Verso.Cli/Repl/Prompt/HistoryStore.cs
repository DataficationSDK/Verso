namespace Verso.Cli.Repl.Prompt;

/// <summary>
/// Resolves the path PrettyPrompt uses for its persistent history file. Follows
/// XDG conventions on Linux/macOS (<c>$XDG_STATE_HOME/verso/repl-history</c>) and
/// the local app-data folder on Windows.
/// </summary>
public static class HistoryStore
{
    /// <summary>
    /// Resolves the history file path. Returns <see langword="null"/> when history
    /// is disabled (the caller passed <c>--history none</c>).
    /// </summary>
    /// <param name="override">CLI <c>--history</c> value, or <see langword="null"/> for the default.</param>
    /// <param name="disabled">True when the caller explicitly passed <c>--history none</c>.</param>
    public static string? Resolve(string? @override, bool disabled)
    {
        if (disabled) return null;
        if (!string.IsNullOrEmpty(@override))
            return Path.GetFullPath(@override);

        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (!string.IsNullOrEmpty(xdg))
                root = xdg;
            else
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        }

        var dir = Path.Combine(root, "verso");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "repl-history");
    }
}
