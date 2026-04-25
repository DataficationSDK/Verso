namespace Verso.Cli.Repl.Prompt;

/// <summary>
/// Runtime detection of terminal features relevant to prompt selection. Falls back
/// to the plain driver when stdin is redirected, <c>TERM=dumb</c>, or <c>NO_COLOR</c>
/// is set in the environment.
/// </summary>
public static class TerminalCapabilities
{
    /// <summary>
    /// True when the PrettyPrompt driver can run: a real TTY on stdin/stdout with
    /// ANSI-capable terminal. Returns false in redirected / piped / dumb scenarios.
    /// </summary>
    public static bool SupportsPrettyPrompt()
    {
        if (Console.IsInputRedirected) return false;
        if (Console.IsOutputRedirected) return false;

        var term = Environment.GetEnvironmentVariable("TERM");
        if (string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase)) return false;

        // CI environments typically set these and usually mean "don't animate a UI"
        // but PrettyPrompt needs a real TTY regardless, so we already filtered above.
        return true;
    }

    /// <summary>True when the terminal should use coloured output.</summary>
    public static bool SupportsColor(bool noColorFlag)
    {
        if (noColorFlag) return false;
        if (Console.IsOutputRedirected) return false;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) return false;
        var term = Environment.GetEnvironmentVariable("TERM");
        if (string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
