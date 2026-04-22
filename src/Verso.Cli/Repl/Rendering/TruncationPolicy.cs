using Verso.Cli.Repl.Settings;

namespace Verso.Cli.Repl.Rendering;

/// <summary>
/// Row- and line-truncation rules for cell outputs. Honors
/// <see cref="ReplSettings"/> values that can be mutated at runtime via <c>.set</c>.
/// </summary>
public readonly record struct TruncationPolicy(int MaxRows, int MaxLines, int MaxWidth)
{
    public static TruncationPolicy FromSettings(ReplSettings settings)
    {
        var width = Console.IsOutputRedirected ? int.MaxValue : Math.Max(40, Console.BufferWidth - 2);
        return new TruncationPolicy(settings.Preview.Rows, settings.Preview.Lines, width);
    }

    /// <summary>
    /// Trims a plain-text block to at most <see cref="MaxLines"/> lines, appending a
    /// footer line when content was dropped. Lines are preserved as-is; no wrapping.
    /// </summary>
    public string ClipLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var lines = text.Split('\n');
        if (lines.Length <= MaxLines) return text;
        var kept = lines.Take(MaxLines);
        return string.Join('\n', kept) + $"\n… {lines.Length - MaxLines} more lines";
    }
}
