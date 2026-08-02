using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.recall</c>. Loads cell N's source into the prompt buffer for editing.</summary>
public sealed class RecallMeta : IMetaCommand
{
    public string Name => "recall";
    public string Summary => Strings.Meta_Recall_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".recall <n>\n" + Strings.Meta_Recall_Details;

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg) || !int.TryParse(arg, out var index) || index <= 0)
        {
            context.Console.MarkupLine(Messages.In("red",
                Messages.Typed(Strings.Meta_Recall_Usage, ".recall <n>", ".history")));
            return Task.FromResult(true);
        }

        var cells = context.Session.Notebook.Cells;
        if (index > cells.Count)
        {
            context.Console.MarkupLine(
                Messages.In("red", Messages.Say(Strings.Meta_Recall_OutOfRange, index))
                + " " + Messages.Say(Strings.Meta_Recall_HistoryContains, CellCount.Describe(cells.Count)));
            return Task.FromResult(true);
        }

        var cell = cells[index - 1];
        context.Session.PendingInitialText = cell.Source;
        context.Console.MarkupLine(Messages.In("dim",
            Messages.Say(Strings.Meta_Recall_Recalled, index)));
        return Task.FromResult(true);
    }
}
