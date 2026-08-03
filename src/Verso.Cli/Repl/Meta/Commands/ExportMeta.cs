using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Execution;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;
using Verso.Contexts;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>
/// Implements <c>.export</c>. Dispatches to an export-menu
/// <see cref="IToolbarAction"/> using the same <see cref="CliToolbarActionContext"/>
/// that backs <c>verso export</c>. Inherits any format registered by any extension.
/// </summary>
public sealed class ExportMeta : IMetaCommand
{
    public string Name => "export";
    public string Summary => Strings.Meta_Export_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp =>
        ".export --format <name> [--output <path>] [--layout <id>] [--theme <name>]\n"
        + Strings.Meta_Export_Details;

    /// <summary>What to type to see the formats installed. The same in every language.</summary>
    private const string ListExportersCommand = ".list exporters";

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        if (!TryParseArgs(argumentText, out var format, out var outputPath, out var layoutId, out var themeName, out var parseError))
        {
            context.Console.MarkupLine(Messages.In("red", Markup.Escape(parseError)));
            return true;
        }

        if (string.IsNullOrEmpty(format))
        {
            context.Console.MarkupLine(
                Messages.In("red", Messages.Say(Strings.Export_FormatRequired))
                + " " + Messages.Typed(Strings.Hint_RunForFormats, ListExportersCommand));
            return true;
        }

        if (!ToolbarActionResolver.TryResolveAction(context.Session.ExtensionHost, format, out var action, out var actionError))
        {
            context.Console.MarkupLine(
                Messages.In("red", Markup.Escape(actionError))
                + " " + Messages.Typed(Strings.Hint_RunForFormats, ListExportersCommand));
            return true;
        }

        ITheme? selectedTheme = context.Session.ActiveTheme;
        if (!string.IsNullOrEmpty(themeName))
        {
            if (!ToolbarActionResolver.TryResolveTheme(context.Session.ExtensionHost, themeName, out selectedTheme, out var themeError))
            {
                context.Console.MarkupLine(Messages.In("red", Markup.Escape(themeError)));
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
            context.Console.MarkupLine(Messages.In("red",
                Messages.Say(Strings.Export_ActionThrew, action.ActionId, ex.Message)));
            return true;
        }

        if (ctx.WrittenPath is null)
        {
            context.Console.MarkupLine(Messages.In("red",
                Messages.Say(Strings.Export_NoFileProduced, action.ActionId)));
            return true;
        }

        context.Console.MarkupLine(Messages.In("green", Messages.Say(
            Strings.Meta_Export_Done, ctx.WrittenPath, CellCount.Describe(context.Session.Notebook.Cells.Count))));
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
                    if (i + 1 >= tokens.Count) { error = string.Format(Strings.Meta_Export_MissingValue, "--format"); return false; }
                    format = tokens[++i];
                    break;
                case "--output":
                case "-o":
                    if (i + 1 >= tokens.Count) { error = string.Format(Strings.Meta_Export_MissingValue, "--output"); return false; }
                    outputPath = Path.GetFullPath(tokens[++i]);
                    break;
                case "--layout":
                    if (i + 1 >= tokens.Count) { error = string.Format(Strings.Meta_Export_MissingValue, "--layout"); return false; }
                    layoutId = tokens[++i];
                    break;
                case "--theme":
                    if (i + 1 >= tokens.Count) { error = string.Format(Strings.Meta_Export_MissingValue, "--theme"); return false; }
                    themeName = tokens[++i];
                    break;
                default:
                    error = string.Format(Strings.Meta_Export_UnknownArgument, tokens[i]);
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
