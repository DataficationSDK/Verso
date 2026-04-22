using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.exit</c> and <c>.quit</c>. Terminates the main loop cleanly.</summary>
public sealed class ExitMeta : IMetaCommand
{
    public string Name => "exit";
    public IReadOnlyList<string> Aliases => new[] { "quit" };
    public string Summary => "Exits the REPL.";
    public string DetailedHelp =>
        ".exit / .quit\n" +
        "  Exits the REPL. When unsaved cells exist, prompts for confirmation\n" +
        "  unless confirmOnExit is disabled in user settings.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        if (context.Session.IsDirty && context.Session.Settings.ConfirmOnExit)
        {
            context.Console.MarkupLine("[yellow]Session has unsaved cells.[/] Type [bold].save[/] first, or [bold].exit[/] again to discard.");
            // Clear dirty so a second .exit without intervening work terminates.
            context.Session.MarkClean();
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
