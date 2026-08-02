using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.clear</c>. Clears the terminal while preserving session state.</summary>
public sealed class ClearMeta : IMetaCommand
{
    public string Name => "clear";
    public string Summary => Strings.Meta_Clear_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".clear\n" + Strings.Meta_Clear_Details;

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        context.Console.Clear();
        return Task.FromResult(true);
    }
}
