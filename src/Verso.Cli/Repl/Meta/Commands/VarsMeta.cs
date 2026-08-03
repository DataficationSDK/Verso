using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.vars</c>. Prints the IVariableStore contents as a Spectre table.</summary>
public sealed class VarsMeta : IMetaCommand
{
    public string Name => "vars";
    public string Summary => Strings.Meta_Vars_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".vars\n" + Strings.Meta_Vars_Details;

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var variables = context.Session.Scaffold.Variables.GetAll();

        if (variables.Count == 0)
        {
            context.Console.MarkupLine(Messages.In("dim", Messages.Say(Strings.Meta_Vars_Empty)));
            return Task.FromResult(true);
        }

        if (context.UseColor)
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn(Strings.Table_Name);
            table.AddColumn(Strings.Table_Type);
            table.AddColumn(Strings.Table_Preview);
            foreach (var v in variables)
            {
                table.AddRow(
                    Markup.Escape(v.Name),
                    Markup.Escape(v.Type.Name),
                    Markup.Escape(PreviewValue(v.Value)));
            }
            context.Console.Write(table);
        }
        else
        {
            Console.Out.WriteLine(
                $"{Truncate(Strings.Table_Name, 20),-20}  {Truncate(Strings.Table_Type, 24),-24}  {Strings.Table_Preview}");
            foreach (var v in variables)
            {
                Console.Out.WriteLine(
                    $"{Truncate(v.Name, 20),-20}  {Truncate(v.Type.Name, 24),-24}  {PreviewValue(v.Value)}");
            }
        }

        return Task.FromResult(true);
    }

    private static string PreviewValue(object? value)
    {
        if (value is null) return "null";
        var text = value.ToString() ?? "";
        return Truncate(text.Replace('\n', ' ').Replace('\r', ' '), 60);
    }

    private static string Truncate(string text, int max)
    {
        if (text.Length <= max) return text;
        return text.Substring(0, Math.Max(0, max - 1)) + "…";
    }
}
