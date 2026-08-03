using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>
/// Implements <c>.convert</c>. Writes the current session notebook to a new path
/// using the serializer selected by the target extension. Matches <c>verso convert</c>.
/// </summary>
public sealed class ConvertMeta : IMetaCommand
{
    public string Name => "convert";
    public string Summary => Strings.Meta_Convert_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".convert <path>\n" + Strings.Meta_Convert_Details;

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            context.Console.MarkupLine(Messages.In("red", Messages.Typed(Strings.Repl_Usage, ".convert <path>")));
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
            context.Console.MarkupLine(Messages.In("green", Messages.Say(
                Strings.Meta_Convert_Done, targetPath, CellCount.Describe(context.Session.Notebook.Cells.Count))));
        }
        catch (Exception ex)
        {
            context.Console.MarkupLine(Messages.In("red",
                Messages.Say(Strings.Meta_Convert_Failed, ex.Message)));
        }

        return true;
    }
}
