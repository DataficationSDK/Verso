using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>
/// Implements <c>.load</c>. Deserializes a notebook from disk and replaces the session
/// notebook. Prior kernel state is preserved (use <c>.reset</c> if a clean slate is needed).
/// </summary>
public sealed class LoadMeta : IMetaCommand
{
    public string Name => "load";
    public string Summary => "Loads a notebook from disk, replacing the session notebook.";
    public string DetailedHelp =>
        ".load <path>\n" +
        "  Deserializes the file at <path> through the matching serializer and installs it\n" +
        "  as the session notebook. Prompts to save unsaved changes first. Kernel state\n" +
        "  (variables) is preserved — run .reset first for a clean start.";

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            context.Console.MarkupLine("[red]Usage: .load <path>[/]");
            return true;
        }

        if (context.Session.IsDirty)
        {
            context.Console.MarkupLine("[yellow]Session has unsaved cells.[/] Run [bold].save[/] first, or [bold].load[/] again to discard.");
            context.Session.MarkClean();
            return true;
        }

        var fullPath = Path.GetFullPath(arg);
        if (!File.Exists(fullPath))
        {
            context.Console.MarkupLine($"[red]File not found: {Markup.Escape(fullPath)}[/]");
            return true;
        }

        INotebookSerializer serializer;
        try
        {
            serializer = SerializerResolver.Resolve(context.Session.ExtensionHost, fullPath);
        }
        catch (SerializerNotFoundException ex)
        {
            context.Console.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
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
            context.Console.MarkupLine($"[red]Failed to load: {Markup.Escape(ex.Message)}[/]");
            return true;
        }

        // Replace session notebook's cells in place so downstream references to
        // context.Session.Notebook remain valid. Title/kernel metadata also copied.
        var current = context.Session.Notebook;
        current.Title = loaded.Title;
        current.Cells.Clear();
        current.Cells.AddRange(loaded.Cells);
        current.DefaultKernelId = loaded.DefaultKernelId ?? current.DefaultKernelId;
        current.ActiveLayoutId = loaded.ActiveLayoutId ?? current.ActiveLayoutId;
        current.PreferredThemeId = loaded.PreferredThemeId ?? current.PreferredThemeId;
        current.FormatVersion = loaded.FormatVersion;
        current.Created = loaded.Created;
        current.Modified = loaded.Modified;

        context.Session.NotebookPath = fullPath;
        context.Session.ActiveKernelId = current.DefaultKernelId ?? context.Session.ActiveKernelId;
        context.Session.MarkClean();

        context.Console.MarkupLine($"[green]Loaded[/] {current.Cells.Count} cell(s) from {Markup.Escape(fullPath)}");
        return true;
    }
}
