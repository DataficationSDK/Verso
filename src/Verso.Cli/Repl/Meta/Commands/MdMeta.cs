using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.md</c>. Marks the next submission as a markdown cell (one-shot).</summary>
public sealed class MdMeta : IMetaCommand
{
    public string Name => "md";
    public string Summary => Strings.Meta_Md_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".md\n" + Strings.Meta_Md_Details;

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        context.Session.NextCellTypeOverride = "markdown";
        context.Console.MarkupLine(Messages.In("dim", Messages.Say(Strings.Meta_Md_Next)));
        return Task.FromResult(true);
    }
}
