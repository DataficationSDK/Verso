using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>
/// Implements <c>.convert</c>. Writes the current session notebook to a new path
/// using the serializer selected by the target extension. Matches <c>verso convert</c>.
/// </summary>
public sealed class ConvertMeta : IMetaCommand
{
    public string Name => "convert";
    public string Summary => "Writes the session notebook to <path> using the serializer matching its extension.";
    public string DetailedHelp =>
        ".convert <path>\n" +
        "  Serializes the current session notebook to <path> using the serializer whose\n" +
        "  FileExtensions include the target extension. Does not change the session's\n" +
        "  loaded path. Identical resolution to 'verso convert'.";

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            context.Console.MarkupLine("[red]Usage: .convert <path>[/]");
            return true;
        }

        var targetPath = Path.GetFullPath(arg);

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
            context.Console.MarkupLine($"[green]Converted[/] session notebook → {Markup.Escape(targetPath)} ({context.Session.Notebook.Cells.Count} cells)");
        }
        catch (Exception ex)
        {
            context.Console.MarkupLine($"[red]Convert failed: {Markup.Escape(ex.Message)}[/]");
        }

        return true;
    }
}
