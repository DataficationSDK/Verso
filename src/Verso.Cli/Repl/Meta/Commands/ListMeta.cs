using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.list</c>. Tables of registered extension capabilities.</summary>
public sealed class ListMeta : IMetaCommand
{
    public string Name => "list";
    public string Summary => Strings.Meta_List_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".list <kind>\n" + Strings.Meta_List_Details;

    /// <summary>What .list accepts. Typed at a keyboard, so the same in every language.</summary>
    private const string Kinds = "kernels, themes, formatters, renderers, serializers, extensions, exporters";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var kind = argumentText.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(kind))
        {
            context.Console.MarkupLine(Messages.In("yellow",
                Messages.Typed(Strings.Repl_Usage, ".list <" + Kinds.Replace(", ", "|") + ">")));
            return Task.FromResult(true);
        }

        var host = context.Session.ExtensionHost;
        switch (kind)
        {
            case "kernels":
                RenderTable(context, new[] { Strings.Table_Language, Strings.Table_Description },
                    host.GetKernels().Select(k => new[] { k.LanguageId, k.Description ?? "" }));
                break;

            case "themes":
                RenderTable(context, new[] { Strings.Table_Theme, Strings.Table_Kind, Strings.Table_Description },
                    host.GetThemes().Select(t => new[] { t.DisplayName, t.ThemeKind.ToString(), t.Description ?? "" }));
                break;

            case "formatters":
                RenderTable(context, new[] { Strings.Table_Name, Strings.Table_Description, Strings.Table_Priority },
                    host.GetFormatters().Select(f => new[] { f.Name, f.Description ?? "", f.Priority.ToString() }));
                break;

            case "renderers":
                RenderTable(context, new[] { Strings.Table_Name, Strings.Table_Description },
                    host.GetRenderers().Select(r => new[] { r.Name, r.Description ?? "" }));
                break;

            case "serializers":
                RenderTable(context, new[] { Strings.Table_Format, Strings.Table_Extensions, Strings.Table_Name },
                    host.GetSerializers().Select(s => new[] { s.FormatId, string.Join(", ", s.FileExtensions), s.Name }));
                break;

            case "extensions":
                RenderTable(context, new[] { Strings.Table_Id, Strings.Table_Name, Strings.Table_Version, Strings.Table_Status },
                    host.GetExtensionInfos().Select(e => new[] { e.ExtensionId, e.Name, e.Version, e.Status.ToString() }));
                break;

            case "exporters":
                RenderTable(context, new[] { Strings.Table_Format, Strings.Table_Description },
                    host.GetToolbarActions()
                        .Where(a => a.Placement == ToolbarPlacement.ExportMenu)
                        .OrderBy(a => a.Order)
                        .Select(a => new[] { a.DisplayName, a.Description ?? "" }));
                break;

            default:
                context.Console.MarkupLine(
                    Messages.In("red", Messages.Say(Strings.Meta_List_UnknownKind, kind))
                    + " " + Messages.Typed(Strings.Meta_List_ValidKinds, Kinds));
                break;
        }

        return Task.FromResult(true);
    }

    private static void RenderTable(MetaContext context, string[] columns, IEnumerable<string[]> rows)
    {
        var rowList = rows.ToList();
        if (rowList.Count == 0)
        {
            context.Console.MarkupLine(Messages.In("dim", Messages.Say(Strings.Meta_List_Empty)));
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
                widths[i] = Math.Max(
                    DisplayWidth.Measure(columns[i]),
                    rowList.Max(r => i < r.Length ? DisplayWidth.Measure(r[i]) : 0));

            // In no-color mode Spectre's segment wrapping concatenates rows; use plain
            // Console.Out so each row lands on its own line.
            Console.Out.WriteLine(string.Join("  ", columns.Select((c, i) => DisplayWidth.PadRight(c, widths[i]))));
            foreach (var row in rowList)
                Console.Out.WriteLine(string.Join("  ", row.Select((c, i) => DisplayWidth.PadRight(c, widths[i]))));
        }
    }
}
