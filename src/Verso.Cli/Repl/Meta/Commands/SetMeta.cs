using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.set</c>. Mutates in-memory REPL settings via dotted key-path.</summary>
public sealed class SetMeta : IMetaCommand
{
    public string Name => "set";
    public string Summary => Strings.Meta_Set_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => Usage + "\n" + Strings.Meta_Set_Details;

    /// <summary>The shape a .set takes. Typed at a keyboard, so the same in every language.</summary>
    private const string Usage = ".set <key> <value>";

    /// <summary>The settings .set understands. Identifiers, so the same in every language.</summary>
    private const string Keys = "preview.rows, preview.lines, preview.elapsedThresholdMs, confirmOnExit";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var parts = argumentText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            context.Console.MarkupLine(
                Messages.In("red", Messages.Typed(Strings.Repl_Usage, Usage))
                + " " + Messages.Typed(Strings.Meta_Set_HelpHint, ".help set"));
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
                    context.Console.MarkupLine(Messages.In("red",
                        Messages.Typed(Strings.Meta_Set_InvalidInteger, "preview.rows", value)));
                break;

            case "preview.lines":
                if (int.TryParse(value, out var lines) && lines > 0)
                {
                    settings.Preview.Lines = lines;
                    context.Console.MarkupLine($"preview.lines = [bold]{lines}[/]");
                }
                else
                    context.Console.MarkupLine(Messages.In("red",
                        Messages.Typed(Strings.Meta_Set_InvalidInteger, "preview.lines", value)));
                break;

            case "preview.elapsedthresholdms":
                if (int.TryParse(value, out var ms) && ms >= 0)
                {
                    settings.Preview.ElapsedThresholdMs = ms;
                    context.Console.MarkupLine($"preview.elapsedThresholdMs = [bold]{ms}[/]");
                }
                else
                    context.Console.MarkupLine(Messages.In("red",
                        Messages.Typed(Strings.Meta_Set_InvalidNonNegative, "preview.elapsedThresholdMs", value)));
                break;

            case "confirmonexit":
                if (bool.TryParse(value, out var confirm))
                {
                    settings.ConfirmOnExit = confirm;
                    context.Console.MarkupLine($"confirmOnExit = [bold]{confirm}[/]");
                }
                else
                    context.Console.MarkupLine(Messages.In("red",
                        Messages.Typed(Strings.Meta_Set_InvalidBoolean, "confirmOnExit", value)));
                break;

            default:
                context.Console.MarkupLine(
                    Messages.In("red", Messages.Say(Strings.Meta_Set_UnknownKey, key))
                    + " " + Messages.Typed(Strings.Meta_Set_KnownKeys, Keys));
                break;
        }

        return Task.FromResult(true);
    }
}
