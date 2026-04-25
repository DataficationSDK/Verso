using Spectre.Console;
using Verso.Abstractions;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.list</c>. Tables of registered extension capabilities.</summary>
public sealed class ListMeta : IMetaCommand
{
    public string Name => "list";
    public string Summary => "Lists registered extension capabilities.";
    public string DetailedHelp =>
        ".list <kind>\n" +
        "  Where <kind> is one of:\n" +
        "    kernels, themes, formatters, renderers, serializers, extensions, exporters\n" +
        "  Prints a Spectre-styled table of the registered items for that capability.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var kind = argumentText.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(kind))
        {
            context.Console.MarkupLine("[yellow]Usage:[/] .list <kernels|themes|formatters|renderers|serializers|extensions|exporters>");
            return Task.FromResult(true);
        }

        var host = context.Session.ExtensionHost;
        switch (kind)
        {
            case "kernels":
                RenderTable(context, new[] { "Language", "Description" },
                    host.GetKernels().Select(k => new[] { k.LanguageId, k.Description ?? "" }));
                break;

            case "themes":
                RenderTable(context, new[] { "Theme", "Kind", "Description" },
                    host.GetThemes().Select(t => new[] { t.DisplayName, t.ThemeKind.ToString(), t.Description ?? "" }));
                break;

            case "formatters":
                RenderTable(context, new[] { "Name", "Description", "Priority" },
                    host.GetFormatters().Select(f => new[] { f.Name, f.Description ?? "", f.Priority.ToString() }));
                break;

            case "renderers":
                RenderTable(context, new[] { "Name", "Description" },
                    host.GetRenderers().Select(r => new[] { r.Name, r.Description ?? "" }));
                break;

            case "serializers":
                RenderTable(context, new[] { "Format", "Extensions", "Name" },
                    host.GetSerializers().Select(s => new[] { s.FormatId, string.Join(", ", s.FileExtensions), s.Name }));
                break;

            case "extensions":
                RenderTable(context, new[] { "Id", "Name", "Version", "Status" },
                    host.GetExtensionInfos().Select(e => new[] { e.ExtensionId, e.Name, e.Version, e.Status.ToString() }));
                break;

            case "exporters":
                RenderTable(context, new[] { "Format", "Description" },
                    host.GetToolbarActions()
                        .Where(a => a.Placement == ToolbarPlacement.ExportMenu)
                        .OrderBy(a => a.Order)
                        .Select(a => new[] { a.DisplayName, a.Description ?? "" }));
                break;

            default:
                context.Console.MarkupLine($"[red]Unknown list kind '{Markup.Escape(kind)}'.[/] Valid: kernels, themes, formatters, renderers, serializers, extensions, exporters.");
                break;
        }

        return Task.FromResult(true);
    }

    private static void RenderTable(MetaContext context, string[] columns, IEnumerable<string[]> rows)
    {
        var rowList = rows.ToList();
        if (rowList.Count == 0)
        {
            context.Console.MarkupLine("[dim]No items registered.[/]");
            return;
        }

        if (context.UseColor)
        {
            var table = new Table().Border(TableBorder.Rounded);
            foreach (var col in columns) table.AddColumn(col);
            foreach (var row in rowList)
                table.AddRow(row.Select(Markup.Escape).ToArray());
            context.Console.Write(table);
        }
        else
        {
            var widths = new int[columns.Length];
            for (int i = 0; i < columns.Length; i++)
                widths[i] = Math.Max(columns[i].Length, rowList.Max(r => i < r.Length ? r[i].Length : 0));

            // In no-color mode Spectre's segment wrapping concatenates rows; use plain
            // Console.Out so each row lands on its own line.
            Console.Out.WriteLine(string.Join("  ", columns.Select((c, i) => c.PadRight(widths[i]))));
            foreach (var row in rowList)
                Console.Out.WriteLine(string.Join("  ", row.Select((c, i) => c.PadRight(widths[i]))));
        }
    }
}
