using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.history</c>. Prints the last N submitted cells.</summary>
public sealed class HistoryMeta : IMetaCommand
{
    public string Name => "history";
    public string Summary => "Prints recent cell submissions.";
    public string DetailedHelp =>
        ".history [<n>]\n" +
        "  Prints the last n submitted cells (default 20). Each entry shows the input counter\n" +
        "  and a preview of the first non-empty line of source.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var n = 20;
        var arg = argumentText.Trim();
        if (!string.IsNullOrEmpty(arg))
        {
            if (!int.TryParse(arg, out n) || n <= 0)
            {
                context.Console.MarkupLine($"[red]Invalid count '{Markup.Escape(arg)}'.[/] Usage: .history [<n>]");
                return Task.FromResult(true);
            }
        }

        var cells = context.Session.Notebook.Cells;
        var start = Math.Max(0, cells.Count - n);

        if (cells.Count == 0)
        {
            context.Console.MarkupLine("[dim]No history.[/]");
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
