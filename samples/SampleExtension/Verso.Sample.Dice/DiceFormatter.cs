using System.Text;
using System.Web;
using Verso.Abstractions;
using Verso.Sample.Dice.Models;

namespace Verso.Sample.Dice;

/// <summary>
/// Formats <see cref="DiceResult"/> objects as styled HTML tables.
/// </summary>
[VersoExtension]
public sealed class DiceFormatter : IDataFormatter
{
    public string ExtensionId => "com.verso.sample.dice.formatter";
    public string Name => "Dice Formatter";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Formats DiceResult objects as HTML tables";

    public IReadOnlyList<Type> SupportedTypes => new[] { typeof(DiceResult) };
    public int Priority => 10;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public bool CanFormat(object value, IFormatterContext context) => value is DiceResult;

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var result = (DiceResult)value;
        var sb = new StringBuilder();

        sb.Append("<table style=\"border-collapse:collapse;font-family:monospace;\">");
        sb.Append("<thead><tr style=\"background:#e3f2fd;\">");
        sb.Append($"<th style=\"padding:4px 12px;border:1px solid #ccc;\" colspan=\"{result.Rolls.Count + 1}\">");
        sb.Append(HttpUtility.HtmlEncode(result.Notation.ToString()));
        sb.Append("</th></tr></thead>");

        sb.Append("<tbody><tr>");
        foreach (var roll in result.Rolls)
        {
            var isMax = roll == result.Notation.Sides;
            var isMin = roll == 1;
            var color = isMax ? "color:#2e7d32;font-weight:bold;" : isMin ? "color:#c62828;font-weight:bold;" : "";
            sb.Append($"<td style=\"padding:4px 12px;border:1px solid #ccc;text-align:center;{color}\">{roll}</td>");
        }

        if (result.Modifier != 0)
        {
            var sign = result.Modifier > 0 ? "+" : "";
            sb.Append($"<td style=\"padding:4px 12px;border:1px solid #ccc;text-align:center;font-style:italic;\">{sign}{result.Modifier}</td>");
        }

        sb.Append("</tr></tbody>");

        sb.Append("<tfoot><tr style=\"background:#f5f5f5;\">");
        sb.Append($"<td style=\"padding:4px 12px;border:1px solid #ccc;text-align:right;font-weight:bold;\" colspan=\"{result.Rolls.Count + 1}\">");
        sb.Append($"Total: {result.Total}");
        sb.Append("</td></tr></tfoot>");

        sb.Append("</table>");

        return Task.FromResult(new CellOutput("text/html", sb.ToString()));
    }
}
