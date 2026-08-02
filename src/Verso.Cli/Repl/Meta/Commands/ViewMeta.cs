using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.view</c>. Prints the full output of the last cell without truncation.</summary>
public sealed class ViewMeta : IMetaCommand
{
    public string Name => "view";
    public string Summary => Strings.Meta_View_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".view [<n>]\n" + Strings.Meta_View_Details;

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var cells = context.Session.Notebook.Cells;
        if (cells.Count == 0)
        {
            context.Console.MarkupLine(Messages.In("dim", Messages.Say(Strings.Meta_View_Nothing)));
            return Task.FromResult(true);
        }

        int index = cells.Count - 1;
        var arg = argumentText.Trim();
        if (!string.IsNullOrEmpty(arg))
        {
            if (!int.TryParse(arg, out var parsed) || parsed <= 0 || parsed > cells.Count)
            {
                context.Console.MarkupLine(Messages.In("red",
                    Messages.Say(Strings.Meta_View_InvalidIndex, arg)));
                return Task.FromResult(true);
            }
            index = parsed - 1;
        }

        var cell = cells[index];
        if (cell.Outputs.Count == 0)
        {
            context.Console.MarkupLine(Messages.In("dim",
                Messages.Say(Strings.Meta_View_NoOutputs, index + 1)));
            return Task.FromResult(true);
        }

        foreach (var output in cell.Outputs)
        {
            context.Console.WriteLine(output.Content);
        }
        return Task.FromResult(true);
    }
}
