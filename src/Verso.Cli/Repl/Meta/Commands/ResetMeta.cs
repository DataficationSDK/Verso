using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.reset</c>. Rebuilds the Scaffold (clearing kernel state) without discarding cell history.</summary>
public sealed class ResetMeta : IMetaCommand
{
    public string Name => "reset";
    public string Summary => "Resets kernel state; keeps cell history.";
    public string DetailedHelp =>
        ".reset\n" +
        "  Rebuilds the kernel session, clearing all variables and runtime state.\n" +
        "  The notebook's cell history (cells already typed) is preserved, so .save\n" +
        "  still captures them. Variables declared before .reset are gone.";

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        await context.Session.ResetScaffoldAsync();
        context.Console.MarkupLine("[green]Kernel state reset.[/]");
        return true;
    }
}
