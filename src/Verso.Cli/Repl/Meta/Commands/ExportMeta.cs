using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Execution;
using Verso.Contexts;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>
/// Implements <c>.export</c> and <c>.publish</c>. Dispatches to an export-menu
/// <see cref="IToolbarAction"/> using the same <see cref="CliToolbarActionContext"/>
/// that backs <c>verso publish</c>. Inherits any format registered by any extension.
/// </summary>
public sealed class ExportMeta : IMetaCommand
{
    public string Name { get; }
    public IReadOnlyList<string> Aliases { get; }
    public string Summary => "Exports the session notebook via an ExportMenu toolbar action.";
    public string DetailedHelp =>
        $".{Name} --format <name> [--output <path>] [--layout <id>] [--theme <name>]\n" +
        "  Dispatches to an IToolbarAction registered with ToolbarPlacement.ExportMenu.\n" +
        "  Format is matched by DisplayName (case-insensitive), ActionId as fallback.\n" +
        "  Theme is matched by DisplayName (case-insensitive), ThemeId as fallback.\n" +
        "  Without --output, writes the action's suggested filename to the current directory.\n" +
        "  Identical to 'verso publish'.";

    /// <summary>
    /// Constructor taking a name enables registering the same implementation twice
    /// under <c>.export</c> and its alias <c>.publish</c> per spec §6.
    /// </summary>
    public ExportMeta(string name = "export", params string[] aliases)
    {
        Name = name;
        Aliases = aliases;
    }

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        if (!TryParseArgs(argumentText, out var format, out var outputPath, out var layoutId, out var themeName, out var parseError))
        {
            context.Console.MarkupLine($"[red]{Markup.Escape(parseError)}[/]");
            return true;
        }

        if (string.IsNullOrEmpty(format))
        {
            context.Console.MarkupLine("[red]--format is required.[/] Run [bold].list exporters[/] to see available formats.");
            return true;
        }

        if (!ToolbarActionResolver.TryResolveAction(context.Session.ExtensionHost, format, out var action, out var actionError))
        {
            context.Console.MarkupLine($"[red]{Markup.Escape(actionError)}[/] Run [bold].list exporters[/] to see available formats.");
            return true;
        }

        ITheme? selectedTheme = context.Session.ActiveTheme;
        if (!string.IsNullOrEmpty(themeName))
        {
            if (!ToolbarActionResolver.TryResolveTheme(context.Session.ExtensionHost, themeName, out selectedTheme, out var themeError))
            {
                context.Console.MarkupLine($"[red]{Markup.Escape(themeError)}[/]");
                return true;
            }
        }

        var effectiveLayout = layoutId ?? context.Session.ActiveLayoutId;
        var metadata = new NotebookMetadataContext(context.Session.Notebook, context.Session.NotebookPath);

        var ctx = new CliToolbarActionContext(
            context.Session.Notebook.Cells,
            metadata,
            context.Session.ExtensionHost,
            selectedTheme,
            effectiveLayout,
            outputPath,
            ct);

        try
        {
            await action.ExecuteAsync(ctx);
        }
        catch (Exception ex)
        {
            context.Console.MarkupLine($"[red]Export action '{Markup.Escape(action.ActionId)}' threw: {Markup.Escape(ex.Message)}[/]");
            return true;
        }

        if (ctx.WrittenPath is null)
        {
            context.Console.MarkupLine($"[red]Export action '{Markup.Escape(action.ActionId)}' did not produce a file.[/]");
            return true;
        }

        context.Console.MarkupLine($"[green]Published[/] session notebook → {Markup.Escape(ctx.WrittenPath)} ({context.Session.Notebook.Cells.Count} cells)");
        return true;
    }

    private static bool TryParseArgs(
        string argumentText,
        out string? format,
        out string? outputPath,
        out string? layoutId,
        out string? themeName,
        out string error)
    {
        format = null;
        outputPath = null;
        layoutId = null;
        themeName = null;
        error = "";

        var tokens = Tokenize(argumentText);
        for (int i = 0; i < tokens.Count; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "--format":
                case "-f":
                    if (i + 1 >= tokens.Count) { error = "Missing value for --format."; return false; }
                    format = tokens[++i];
                    break;
                case "--output":
                case "-o":
                    if (i + 1 >= tokens.Count) { error = "Missing value for --output."; return false; }
                    outputPath = Path.GetFullPath(tokens[++i]);
                    break;
                case "--layout":
                    if (i + 1 >= tokens.Count) { error = "Missing value for --layout."; return false; }
                    layoutId = tokens[++i];
                    break;
                case "--theme":
                    if (i + 1 >= tokens.Count) { error = "Missing value for --theme."; return false; }
                    themeName = tokens[++i];
                    break;
                default:
                    error = $"Unknown argument: {tokens[i]}";
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Simple quoted-string-aware tokenizer so <c>--format "Power BI"</c> keeps the
    /// value together. Handles double-quoted spans; escapes not supported in v1.0.
    /// </summary>
    private static List<string> Tokenize(string input)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(input)) return result;
        var i = 0;
        var buffer = new System.Text.StringBuilder();

        while (i < input.Length)
        {
            var ch = input[i];
            if (char.IsWhiteSpace(ch))
            {
                if (buffer.Length > 0) { result.Add(buffer.ToString()); buffer.Clear(); }
                i++;
                continue;
            }
            if (ch == '"')
            {
                i++;
                while (i < input.Length && input[i] != '"')
                {
                    buffer.Append(input[i]);
                    i++;
                }
                if (i < input.Length) i++;
                result.Add(buffer.ToString());
                buffer.Clear();
                continue;
            }
            buffer.Append(ch);
            i++;
        }
        if (buffer.Length > 0) result.Add(buffer.ToString());
        return result;
    }
}
