using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.save</c>. Serializes the session notebook via the matching <see cref="INotebookSerializer"/>.</summary>
public sealed class SaveMeta : IMetaCommand
{
    public string Name => "save";
    public string Summary => Strings.Meta_Save_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".save [<path>]\n" + Strings.Meta_Save_Details;

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        var explicitTarget = !string.IsNullOrEmpty(arg);
        var targetPath = explicitTarget ? Path.GetFullPath(arg) : context.Session.NotebookPath;

        if (string.IsNullOrEmpty(targetPath))
        {
            context.Console.MarkupLine(
                Messages.In("red", Messages.Say(Strings.Meta_Save_NeedsPath))
                + " " + Messages.Typed(Strings.Repl_Usage, ".save <path>"));
            return true;
        }

        // Implicit-target saves on a non-.verso path: when the user has not asked to preserve
        // the original format, route to a sibling .verso file (matching the VS Code default).
        // Explicit `.save foo.ipynb` always honors the path the user typed. Formats whose
        // serializer preserves by default (e.g. Markdown) skip the conversion entirely.
        var preservesByDefault = context.Session.ExtensionHost.GetSerializers()
            .Any(s => s.CanImport(targetPath) && s.PreservesFormatByDefault);

        if (!explicitTarget
            && !context.Session.PreserveFormat
            && !preservesByDefault
            && !targetPath.EndsWith(".verso", StringComparison.OrdinalIgnoreCase))
        {
            context.Console.MarkupLine(Messages.In("yellow",
                Messages.Say(Strings.Meta_Save_Converting, Path.GetExtension(targetPath))));
            targetPath = Path.ChangeExtension(targetPath, ".verso");
        }

        INotebookSerializer serializer;
        try
        {
            serializer = SerializerResolver.Resolve(context.Session.ExtensionHost, targetPath);
        }
        catch (SerializerNotFoundException ex)
        {
            context.Console.MarkupLine(Messages.In("red", Markup.Escape(ex.Message)));
            return true;
        }

        try
        {
            var content = await serializer.SerializeAsync(context.Session.Notebook);
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(targetPath, content, ct);
            context.Session.NotebookPath = targetPath;
            context.Session.MarkClean();
            context.Console.MarkupLine(Messages.In("green", Messages.Say(
                Strings.Meta_Save_Done, CellCount.Describe(context.Session.Notebook.Cells.Count), targetPath)));
        }
        catch (Exception ex)
        {
            context.Console.MarkupLine(Messages.In("red",
                Messages.Say(Strings.Meta_Save_Failed, ex.Message)));
        }

        return true;
    }
}
