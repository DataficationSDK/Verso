using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>
/// Implements <c>.load</c>. Deserializes a notebook from disk and replaces the session
/// notebook. Prior kernel state is preserved (use <c>.reset</c> if a clean slate is needed).
/// </summary>
public sealed class LoadMeta : IMetaCommand
{
    public string Name => "load";
    public string Summary => Strings.Meta_Load_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".load <path>\n" + Strings.Meta_Load_Details;

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            context.Console.MarkupLine(Messages.In("red", Messages.Typed(Strings.Repl_Usage, ".load <path>")));
            return true;
        }

        if (!context.Session.ConfirmDiscardUnsavedChanges())
        {
            context.Console.MarkupLine(
                Messages.In("yellow", Messages.Say(Strings.Repl_UnsavedCells))
                + " " + Messages.Typed(Strings.Repl_UnsavedHint, ".save", ".load"));
            return true;
        }

        var fullPath = Path.GetFullPath(arg);
        if (!File.Exists(fullPath))
        {
            context.Console.MarkupLine(Messages.In("red",
                Messages.Say(Strings.Meta_Load_FileNotFound, fullPath)));
            return true;
        }

        INotebookSerializer serializer;
        try
        {
            serializer = SerializerResolver.Resolve(context.Session.ExtensionHost, fullPath);
        }
        catch (SerializerNotFoundException ex)
        {
            context.Console.MarkupLine(Messages.In("red", Markup.Escape(ex.Message)));
            return true;
        }

        NotebookModel loaded;
        try
        {
            var content = await File.ReadAllTextAsync(fullPath, ct);
            loaded = await serializer.DeserializeAsync(content);
        }
        catch (Exception ex)
        {
            context.Console.MarkupLine(Messages.In("red",
                Messages.Say(Strings.Meta_Load_Failed, ex.Message)));
            return true;
        }

        // Replace session notebook's cells in place so downstream references to
        // context.Session.Notebook remain valid. Title/kernel metadata also copied.
        var current = context.Session.Notebook;
        current.Title = loaded.Title;
        current.Cells.Clear();
        current.Cells.AddRange(loaded.Cells);
        current.DefaultKernelId = loaded.DefaultKernelId ?? current.DefaultKernelId;
        current.ActiveLayout = loaded.ActiveLayout ?? current.ActiveLayout;
        current.RequiresLegacyLayoutResolution = loaded.RequiresLegacyLayoutResolution;
        current.PreferredThemeId = loaded.PreferredThemeId ?? current.PreferredThemeId;
        current.FormatVersion = loaded.FormatVersion;
        current.Created = loaded.Created;
        current.Modified = loaded.Modified;

        context.Session.NotebookPath = fullPath;
        context.Session.ActiveKernelId = current.DefaultKernelId ?? context.Session.ActiveKernelId;
        context.Session.MarkClean();

        context.Console.MarkupLine(Messages.In("green", Messages.Say(
            Strings.Meta_Load_Done, CellCount.Describe(current.Cells.Count), fullPath)));
        return true;
    }
}
