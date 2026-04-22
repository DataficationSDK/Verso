using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.vars</c>. Prints the IVariableStore contents as a Spectre table.</summary>
public sealed class VarsMeta : IMetaCommand
{
    public string Name => "vars";
    public string Summary => "Lists variables from IVariableStore.";
    public string DetailedHelp =>
        ".vars [<kernel-id>]\n" +
        "  Lists variables from the shared IVariableStore. Columns: Name, Kernel, Type, Preview.\n" +
        "  With a kernel-id argument, filters to variables tagged with that kernel.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var filter = argumentText.Trim();
        var variables = context.Session.Scaffold.Variables.GetAll();
        if (!string.IsNullOrEmpty(filter))
        {
            variables = variables
                .Where(v => string.Equals(v.KernelId, filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (variables.Count == 0)
        {
            context.Console.MarkupLine("[dim]No variables.[/]");
            return Task.FromResult(true);
        }

        if (context.UseColor)
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Name");
            table.AddColumn("Kernel");
            table.AddColumn("Type");
            table.AddColumn("Preview");
            foreach (var v in variables)
            {
                table.AddRow(
                    Markup.Escape(v.Name),
                    Markup.Escape(v.KernelId ?? ""),
                    Markup.Escape(v.Type.Name),
                    Markup.Escape(PreviewValue(v.Value)));
            }
            context.Console.Write(table);
        }
        else
        {
            Console.Out.WriteLine($"{"NAME",-20}  {"KERNEL",-12}  {"TYPE",-24}  PREVIEW");
            foreach (var v in variables)
            {
                Console.Out.WriteLine(
                    $"{Truncate(v.Name, 20),-20}  {Truncate(v.KernelId ?? "", 12),-12}  {Truncate(v.Type.Name, 24),-24}  {PreviewValue(v.Value)}");
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
