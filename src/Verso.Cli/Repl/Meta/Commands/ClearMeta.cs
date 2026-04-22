using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.clear</c>. Clears the terminal while preserving session state.</summary>
public sealed class ClearMeta : IMetaCommand
{
    public string Name => "clear";
    public string Summary => "Clears the terminal.";
    public string DetailedHelp =>
        ".clear\n" +
        "  Clears the terminal screen. Session state (kernel variables, notebook cells)\n" +
        "  is preserved; only the scrollback is cleared.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        context.Console.Clear();
        return Task.FromResult(true);
    }
}
