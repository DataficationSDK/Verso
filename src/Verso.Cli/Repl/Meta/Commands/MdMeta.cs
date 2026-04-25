using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.md</c>. Marks the next submission as a markdown cell (one-shot).</summary>
public sealed class MdMeta : IMetaCommand
{
    public string Name => "md";
    public string Summary => "Marks the next submission as a markdown cell.";
    public string DetailedHelp =>
        ".md\n" +
        "  One-shot: the next submission is appended as a markdown cell instead of a code cell.\n" +
        "  After the cell is appended, the REPL reverts to code mode.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        context.Session.NextCellTypeOverride = "markdown";
        context.Console.MarkupLine("[dim]Next cell will be markdown.[/]");
        return Task.FromResult(true);
    }
}
