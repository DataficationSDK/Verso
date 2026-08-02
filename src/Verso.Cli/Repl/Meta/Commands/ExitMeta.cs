using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.exit</c> and <c>.quit</c>. Terminates the main loop cleanly.</summary>
public sealed class ExitMeta : IMetaCommand
{
    public string Name => "exit";
    public IReadOnlyList<string> Aliases => new[] { "quit" };
    public string Summary => Strings.Meta_Exit_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".exit / .quit\n" + Strings.Meta_Exit_Details;

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        if (context.Session.Settings.ConfirmOnExit && !context.Session.ConfirmDiscardUnsavedChanges())
        {
            context.Console.MarkupLine(
                Messages.In("yellow", Messages.Say(Strings.Repl_UnsavedCells))
                + " " + Messages.Typed(Strings.Repl_UnsavedHint, ".save", ".exit"));
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
