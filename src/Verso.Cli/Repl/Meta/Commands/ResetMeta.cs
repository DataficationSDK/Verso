using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.reset</c>. Rebuilds the Scaffold (clearing kernel state) without discarding cell history.</summary>
public sealed class ResetMeta : IMetaCommand
{
    public string Name => "reset";
    public string Summary => Strings.Meta_Reset_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".reset\n" + Strings.Meta_Reset_Details;

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        await context.Session.ResetScaffoldAsync();
        context.Console.MarkupLine(Messages.In("green", Messages.Say(Strings.Meta_Reset_Done)));
        return true;
    }
}
