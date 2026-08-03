using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;
using Verso.Execution;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.rerun</c>. Re-executes cell N (or a range) as new cells appended to the notebook.</summary>
public sealed class RerunMeta : IMetaCommand
{
    public string Name => "rerun";
    public string Summary => Strings.Meta_Rerun_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => Usage + "\n" + Strings.Meta_Rerun_Details;

    /// <summary>The shape a .rerun takes. Typed at a keyboard, so the same in every language.</summary>
    private const string Usage = ".rerun <n>[..<m>]|all [--fail-fast]";

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var parts = argumentText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var failFast = parts.Any(p => string.Equals(p, "--fail-fast", StringComparison.OrdinalIgnoreCase));
        var range = parts.FirstOrDefault(p => !p.StartsWith("--")) ?? "";

        if (string.IsNullOrEmpty(range))
        {
            context.Console.MarkupLine(Messages.In("red", Messages.Typed(Strings.Repl_Usage, Usage)));
            return true;
        }

        var cells = context.Session.Notebook.Cells;
        int start, end;
        if (string.Equals(range, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (cells.Count == 0)
            {
                context.Console.MarkupLine(Messages.In("dim", Messages.Say(Strings.Meta_Rerun_Nothing)));
                return true;
            }
            start = 1;
            end = cells.Count;
        }
        else if (range.Contains(".."))
        {
            var pieces = range.Split("..", 2);
            if (!int.TryParse(pieces[0], out start) || !int.TryParse(pieces[1], out end) || start <= 0 || end < start)
            {
                context.Console.MarkupLine(Messages.In("red",
                    Messages.Say(Strings.Meta_Rerun_InvalidRange, range)));
                return true;
            }
        }
        else
        {
            if (!int.TryParse(range, out start) || start <= 0)
            {
                context.Console.MarkupLine(Messages.In("red",
                    Messages.Say(Strings.Meta_Rerun_InvalidIndex, range)));
                return true;
            }
            end = start;
        }

        if (end > cells.Count)
        {
            context.Console.MarkupLine(Messages.In("red",
                Messages.Say(Strings.Meta_Rerun_RangeTooLong, start, end, cells.Count)));
            return true;
        }

        for (int i = start; i <= end; i++)
        {
            var source = cells[i - 1].Source;
            var language = cells[i - 1].Language ?? context.Session.ActiveKernelId;
            var type = cells[i - 1].Type;

            var newCell = new CellModel
            {
                Type = type,
                Language = type == "code" ? language : null,
                Source = source
            };
            cells.Add(newCell);
            context.Session.MarkDirty();

            var counter = context.Session.NextInputCounter();

            if (string.Equals(type, "markdown", StringComparison.OrdinalIgnoreCase))
            {
                context.Renderer.RenderCell(counter, newCell, ExecutionResult.Success(newCell.Id, 0, TimeSpan.Zero), TimeSpan.FromMilliseconds(200));
                continue;
            }

            ExecutionResult result;
            try
            {
                result = await context.Session.Scaffold.ExecuteCellAsync(newCell.Id, ct);
            }
            catch (OperationCanceledException)
            {
                context.Console.MarkupLine(Messages.In("yellow", Messages.Say(Strings.Repl_Cancelled)));
                break;
            }
            catch (Exception ex)
            {
                context.Console.MarkupLine(Messages.In("red",
                    Messages.Say(Strings.Meta_Rerun_ExecutionError, i, ex.Message)));
                if (failFast) break;
                continue;
            }

            context.Renderer.RenderCell(counter, newCell, result, TimeSpan.FromMilliseconds(200));

            if (failFast && result.Status == ExecutionResult.ExecutionStatus.Failed)
                break;
        }

        return true;
    }
}
