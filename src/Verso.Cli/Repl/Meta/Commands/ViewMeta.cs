using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.view</c>. Prints the full output of the last cell without truncation.</summary>
public sealed class ViewMeta : IMetaCommand
{
    public string Name => "view";
    public string Summary => "Prints the last cell's output without truncation.";
    public string DetailedHelp =>
        ".view [<n>]\n" +
        "  Prints the outputs of the last cell (or cell n if supplied) in full, bypassing\n" +
        "  the row/line caps applied during normal rendering.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var cells = context.Session.Notebook.Cells;
        if (cells.Count == 0)
        {
            context.Console.MarkupLine("[dim]No cells to view.[/]");
            return Task.FromResult(true);
        }

        int index = cells.Count - 1;
        var arg = argumentText.Trim();
        if (!string.IsNullOrEmpty(arg))
        {
            if (!int.TryParse(arg, out var parsed) || parsed <= 0 || parsed > cells.Count)
            {
                context.Console.MarkupLine($"[red]Invalid or out-of-range cell index '{Markup.Escape(arg)}'.[/]");
                return Task.FromResult(true);
            }
            index = parsed - 1;
        }

        var cell = cells[index];
        if (cell.Outputs.Count == 0)
        {
            context.Console.MarkupLine($"[dim]Cell [[{index + 1}]] has no outputs.[/]");
            return Task.FromResult(true);
        }

        foreach (var output in cell.Outputs)
        {
            context.Console.WriteLine(output.Content);
        }
        return Task.FromResult(true);
    }
}
