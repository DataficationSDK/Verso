using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.history</c>. Prints the last N submitted cells.</summary>
public sealed class HistoryMeta : IMetaCommand
{
    public string Name => "history";
    public string Summary => Strings.Meta_History_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".history [<n>]\n" + Strings.Meta_History_Details;

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var n = 20;
        var arg = argumentText.Trim();
        if (!string.IsNullOrEmpty(arg))
        {
            if (!int.TryParse(arg, out n) || n <= 0)
            {
                context.Console.MarkupLine(
                    Messages.In("red", Messages.Say(Strings.Meta_History_InvalidCount, arg))
                    + " " + Messages.Typed(Strings.Repl_Usage, ".history [<n>]"));
                return Task.FromResult(true);
            }
        }

        var cells = context.Session.Notebook.Cells;
        var start = Math.Max(0, cells.Count - n);

        if (cells.Count == 0)
        {
            context.Console.MarkupLine(Messages.In("dim", Messages.Say(Strings.Meta_History_Empty)));
            return Task.FromResult(true);
        }

        for (int i = start; i < cells.Count; i++)
        {
            var cell = cells[i];
            var firstLine = cell.Source.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
            if (firstLine.Length > 80) firstLine = firstLine.Substring(0, 79) + "…";

            var counter = i + 1;
            if (context.UseColor)
                context.Console.MarkupLine($"[dim][[{counter}]][/] {Markup.Escape(firstLine)}");
            else
                context.Console.WriteLine($"[{counter}] {firstLine}");
        }

        return Task.FromResult(true);
    }
}
