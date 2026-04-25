using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.recall</c>. Loads cell N's source into the prompt buffer for editing.</summary>
public sealed class RecallMeta : IMetaCommand
{
    public string Name => "recall";
    public string Summary => "Loads a prior cell's source into the prompt for editing.";
    public string DetailedHelp =>
        ".recall <n>\n" +
        "  Loads cell n's source into the prompt buffer as if the user had typed it.\n" +
        "  Pressing Enter submits it as a new cell; the original cell remains untouched.\n" +
        "  Out-of-range indices produce an error without clearing the buffer.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg) || !int.TryParse(arg, out var index) || index <= 0)
        {
            context.Console.MarkupLine($"[red]Usage: .recall <n>[/] where <n> is a 1-based cell index (from .history).");
            return Task.FromResult(true);
        }

        var cells = context.Session.Notebook.Cells;
        if (index > cells.Count)
        {
            context.Console.MarkupLine($"[red]Cell [[{index}]] is out of range.[/] History contains {cells.Count} cell(s).");
            return Task.FromResult(true);
        }

        var cell = cells[index - 1];
        context.Session.PendingInitialText = cell.Source;
        context.Console.MarkupLine($"[dim]Recalled cell [[{index}]]; edit then press Enter + blank line (or ;;) to submit.[/]");
        return Task.FromResult(true);
    }
}
