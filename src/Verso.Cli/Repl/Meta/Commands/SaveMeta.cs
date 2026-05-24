using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.save</c>. Serializes the session notebook via the matching <see cref="INotebookSerializer"/>.</summary>
public sealed class SaveMeta : IMetaCommand
{
    public string Name => "save";
    public string Summary => "Writes the session notebook to disk.";
    public string DetailedHelp =>
        ".save [<path>]\n" +
        "  Serializes the session notebook. When <path> is omitted, saves to the original\n" +
        "  loaded path (if any) or reports an error. Format is inferred from the extension.\n" +
        "  Without --preserve-format, a .save with no arg against an .ipynb-loaded notebook\n" +
        "  converts to a sibling .verso file; with --preserve-format the original format is kept.";

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        var explicitTarget = !string.IsNullOrEmpty(arg);
        var targetPath = explicitTarget ? Path.GetFullPath(arg) : context.Session.NotebookPath;

        if (string.IsNullOrEmpty(targetPath))
        {
            context.Console.MarkupLine("[red].save requires a path when the session has no loaded notebook.[/] Usage: .save <path>");
            return true;
        }

        // Implicit-target saves on a non-.verso path: when the user has not asked to preserve
        // the original format, route to a sibling .verso file (matching the VS Code default).
        // Explicit `.save foo.ipynb` always honors the path the user typed.
        if (!explicitTarget
            && !context.Session.PreserveFormat
            && !targetPath.EndsWith(".verso", StringComparison.OrdinalIgnoreCase))
        {
            context.Console.MarkupLine(
                $"[yellow]Converting to .verso; use --preserve-format to keep[/] {Markup.Escape(Path.GetExtension(targetPath))}.");
            targetPath = Path.ChangeExtension(targetPath, ".verso");
        }

        INotebookSerializer serializer;
        try
        {
            serializer = SerializerResolver.Resolve(context.Session.ExtensionHost, targetPath);
        }
        catch (SerializerNotFoundException ex)
        {
            context.Console.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
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
            context.Console.MarkupLine($"[green]Saved[/] {context.Session.Notebook.Cells.Count} cell(s) to {Markup.Escape(targetPath)}");
        }
        catch (Exception ex)
        {
            context.Console.MarkupLine($"[red]Failed to save: {Markup.Escape(ex.Message)}[/]");
        }

        return true;
    }
}
