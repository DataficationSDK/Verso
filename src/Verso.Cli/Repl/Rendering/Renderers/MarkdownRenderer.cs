using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;
using Verso.Abstractions;

namespace Verso.Cli.Repl.Rendering.Renderers;

/// <summary>
/// Lightweight terminal markdown: headings are bold+underline, inline emphasis
/// (<c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>) maps to Spectre markup.
/// Rendering inside a Panel precludes using a Rule for headings, so block-level
/// structure is expressed with text styles instead.
/// </summary>
internal static class MarkdownRenderer
{
    private static readonly Regex BoldRe = new(@"\*\*([^\*]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRe = new(@"(?<!\*)\*([^\*]+)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex CodeRe = new(@"`([^`]+)`", RegexOptions.Compiled);

    public static IRenderable AsRenderable(CellOutput output, TruncationPolicy policy, bool useColor)
    {
        var text = policy.ClipLines(output.Content ?? string.Empty);
        if (!useColor)
            return new Text(text);

        var rows = new List<IRenderable>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            rows.Add(BuildLine(line));
        }
        return new Rows(rows.ToArray());
    }

    private static IRenderable BuildLine(string line)
    {
        if (line.StartsWith("#"))
        {
            int level = 0;
            while (level < line.Length && line[level] == '#' && level < 6) level++;
            var heading = line.Substring(level).TrimStart();
            var style = level == 1 ? "bold underline" : "bold";
            return new Markup($"[{style}]{Markup.Escape(heading)}[/]");
        }

        if (line.StartsWith("> "))
            return new Markup($"[dim]│ {Markup.Escape(line.Substring(2))}[/]");

        var escaped = Markup.Escape(line);
        escaped = BoldRe.Replace(escaped, m => $"[bold]{m.Groups[1].Value}[/]");
        escaped = ItalicRe.Replace(escaped, m => $"[italic]{m.Groups[1].Value}[/]");
        escaped = CodeRe.Replace(escaped, m => $"[yellow on grey11]{m.Groups[1].Value}[/]");
        return new Markup(escaped);
    }
}
