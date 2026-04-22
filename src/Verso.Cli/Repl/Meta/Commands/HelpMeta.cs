using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.help</c>. Prints an overview of all meta-commands, or detailed help for one.</summary>
public sealed class HelpMeta : IMetaCommand
{
    public string Name => "help";
    public string Summary => "Prints meta-command help.";
    public string DetailedHelp =>
        ".help [<name>]\n" +
        "  With no argument, prints an overview of all meta-commands.\n" +
        "  With a name, prints detailed help for that command.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var trimmed = argumentText.Trim().TrimStart('.');
        if (!string.IsNullOrEmpty(trimmed))
        {
            if (context.Registry.TryResolve(trimmed, out var command))
            {
                context.Console.WriteLine(command.DetailedHelp);
            }
            else
            {
                context.Console.MarkupLine($"[red]Unknown meta-command '.{trimmed}'.[/] Type [bold].help[/] for the list.");
            }
            return Task.FromResult(true);
        }

        if (context.UseColor)
        {
            var table = new Table { Border = TableBorder.None, ShowHeaders = false };
            table.AddColumn(new TableColumn("").PadRight(2));
            table.AddColumn(new TableColumn(""));
            foreach (var command in context.Registry.AllOrdered)
                table.AddRow($"[bold].{command.Name}[/]", command.Summary);
            context.Console.Write(table);
        }
        else
        {
            foreach (var command in context.Registry.AllOrdered)
                context.Console.WriteLine($".{command.Name}  {command.Summary}");
        }
        return Task.FromResult(true);
    }
}
