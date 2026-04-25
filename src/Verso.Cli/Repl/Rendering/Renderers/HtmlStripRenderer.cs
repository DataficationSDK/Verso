using System.Net;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;
using Verso.Abstractions;

namespace Verso.Cli.Repl.Rendering.Renderers;

/// <summary>
/// Minimal HTML-to-text for terminal output. Strips tags, decodes entities,
/// preserves line breaks from <c>&lt;br&gt;</c> and block-level elements, and
/// drops scripts/styles entirely.
/// </summary>
internal static class HtmlStripRenderer
{
    private static readonly Regex ScriptStyleRe = new(
        @"<(script|style)[^>]*>.*?</\1>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex TagRe = new(@"<[^>]+>", RegexOptions.Compiled);

    public static IRenderable AsRenderable(CellOutput output, TruncationPolicy policy)
    {
        var html = output.Content ?? string.Empty;
        var stripped = ScriptStyleRe.Replace(html, string.Empty);
        stripped = Regex.Replace(stripped, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"</(p|div|h[1-6]|li|tr)>", "\n", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<(li)[^>]*>", "- ", RegexOptions.IgnoreCase);
        stripped = TagRe.Replace(stripped, string.Empty);
        stripped = WebUtility.HtmlDecode(stripped);
        stripped = Regex.Replace(stripped, @"\n{3,}", "\n\n");
        return new Text(policy.ClipLines(stripped.TrimEnd()));
    }
}
