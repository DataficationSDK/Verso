using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.help</c>. Prints an overview of all meta-commands, or detailed help for one.</summary>
public sealed class HelpMeta : IMetaCommand
{
    public string Name => "help";
    public string Summary => Strings.Meta_Help_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".help [<name>]\n" + Strings.Meta_Help_Details;

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
                context.Console.MarkupLine(
                    Messages.In("red", Messages.Say(Strings.Repl_UnknownMetaCommand, trimmed))
                    + " " + Messages.Typed(Strings.Repl_TypeHelpForList, ".help"));
            }
            return Task.FromResult(true);
        }

        if (context.UseColor)
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn(Strings.Table_Command);
            table.AddColumn(Strings.Table_Description);
            foreach (var command in context.Registry.AllOrdered)
                table.AddRow($"[bold].{command.Name}[/]", Markup.Escape(command.Summary));
            context.Console.Write(table);
        }
        else
        {
            // Read once into a local: a column is padded to the width of its own heading,
            // and the heading is not the length it was in English.
            var commandHeading = Strings.Table_Command;
            var width = Math.Max(DisplayWidth.Measure(commandHeading),
                context.Registry.AllOrdered.Max(c => c.Name.Length + 1));
            Console.Out.WriteLine($"{DisplayWidth.PadRight(commandHeading, width)}  {Strings.Table_Description}");
            foreach (var command in context.Registry.AllOrdered)
                Console.Out.WriteLine($"{DisplayWidth.PadRight("." + command.Name, width)}  {command.Summary}");
        }
        return Task.FromResult(true);
    }
}
