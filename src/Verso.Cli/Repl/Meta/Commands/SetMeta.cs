using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.set</c>. Mutates in-memory REPL settings via dotted key-path.</summary>
public sealed class SetMeta : IMetaCommand
{
    public string Name => "set";
    public string Summary => "Sets a runtime REPL setting (preview.rows, preview.lines, preview.elapsedThresholdMs).";
    public string DetailedHelp =>
        ".set <key> <value>\n" +
        "  Updates one of the runtime REPL settings.\n" +
        "  Known keys: preview.rows, preview.lines, preview.elapsedThresholdMs, confirmOnExit.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var parts = argumentText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            context.Console.MarkupLine("[red]Usage: .set <key> <value>[/] — try [bold].help set[/] for key list.");
            return Task.FromResult(true);
        }

        var key = parts[0].ToLowerInvariant();
        var value = parts[1];
        var settings = context.Session.Settings;

        switch (key)
        {
            case "preview.rows":
                if (int.TryParse(value, out var rows) && rows > 0)
                {
                    settings.Preview.Rows = rows;
                    context.Console.MarkupLine($"preview.rows = [bold]{rows}[/]");
                }
                else
                    context.Console.MarkupLine($"[red]Invalid integer for preview.rows: '{Markup.Escape(value)}'[/]");
                break;

            case "preview.lines":
                if (int.TryParse(value, out var lines) && lines > 0)
                {
                    settings.Preview.Lines = lines;
                    context.Console.MarkupLine($"preview.lines = [bold]{lines}[/]");
                }
                else
                    context.Console.MarkupLine($"[red]Invalid integer for preview.lines: '{Markup.Escape(value)}'[/]");
                break;

            case "preview.elapsedthresholdms":
                if (int.TryParse(value, out var ms) && ms >= 0)
                {
                    settings.Preview.ElapsedThresholdMs = ms;
                    context.Console.MarkupLine($"preview.elapsedThresholdMs = [bold]{ms}[/]");
                }
                else
                    context.Console.MarkupLine($"[red]Invalid non-negative integer for preview.elapsedThresholdMs: '{Markup.Escape(value)}'[/]");
                break;

            case "confirmonexit":
                if (bool.TryParse(value, out var confirm))
                {
                    settings.ConfirmOnExit = confirm;
                    context.Console.MarkupLine($"confirmOnExit = [bold]{confirm}[/]");
                }
                else
                    context.Console.MarkupLine($"[red]Invalid boolean for confirmOnExit: '{Markup.Escape(value)}'[/]");
                break;

            default:
                context.Console.MarkupLine($"[red]Unknown setting key '{Markup.Escape(key)}'.[/] Known: preview.rows, preview.lines, preview.elapsedThresholdMs, confirmOnExit.");
                break;
        }

        return Task.FromResult(true);
    }
}
