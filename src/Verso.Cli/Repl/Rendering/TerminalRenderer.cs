using Spectre.Console;
using Spectre.Console.Rendering;
using Verso.Abstractions;
using Verso.Cli.Repl.Settings;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;
using Verso.Execution;
using Verso.Extensions.Utilities;

namespace Verso.Cli.Repl.Rendering;

/// <summary>
/// Renders a cell's outputs to the terminal. Delegates MIME-specific work to
/// <see cref="MimeDispatcher"/>; owns the cell frame (bordered panel with a
/// success/failure glyph) and the elapsed-time hint for slow cells.
/// </summary>
public sealed class TerminalRenderer
{
    private readonly IAnsiConsole _console;
    private readonly bool _useColor;
    private readonly MimeDispatcher _dispatcher;
    private ReplSettings? _settings;

    public TerminalRenderer(IAnsiConsole? console = null, bool useColor = true)
    {
        _console = console ?? AnsiConsole.Console;
        _useColor = useColor;
        _dispatcher = new MimeDispatcher(_console, useColor);
    }

    /// <summary>Rebinds settings so <c>.set preview.*</c> edits affect the next render.</summary>
    public void BindSettings(ReplSettings settings) => _settings = settings;

    /// <summary>
    /// Writes the output block for a single cell: each output rendered and
    /// wrapped in a bordered panel with a success or failure glyph, plus an
    /// elapsed-time hint when the cell exceeded the threshold.
    /// </summary>
    public void RenderCell(int inputCounter, CellModel cell, ExecutionResult result, TimeSpan elapsedThreshold)
    {
        var policy = TruncationPolicy.FromSettings(_settings ?? new ReplSettings());
        var hasError = result.Status == ExecutionResult.ExecutionStatus.Failed || cell.Outputs.Any(o => o.IsError);

        // Honor verso:ui.outputVisibility for cells that carry the metadata (i.e. those
        // loaded from a .verso file). Interactive REPL cells never have this metadata,
        // so the live shell is unaffected. Per spec, "preview" is treated as expanded
        // in the REPL — the REPL is already line-oriented, so we do not truncate here.
        var hideOutputs = string.Equals(
            CellViewStateReader.ReadOutputVisibility(cell),
            CellViewStateMetadata.OutputHidden,
            StringComparison.Ordinal);

        if (hideOutputs)
        {
            // Errors always surface — view-state should not silently swallow failures.
            if (!hasError)
            {
                MaybePrintElapsed(result, elapsedThreshold);
                return;
            }
        }

        if (cell.Outputs.Count == 0 && !hasError)
        {
            MaybePrintElapsed(result, elapsedThreshold);
            return;
        }

        var items = new List<IRenderable>();
        if (!hideOutputs)
        {
            for (int i = 0; i < cell.Outputs.Count; i++)
                items.Add(_dispatcher.AsRenderable(cell.Outputs[i], inputCounter, i, policy));
        }
        else
        {
            // hideOutputs && hasError: emit only the error outputs so users still see failures.
            for (int i = 0; i < cell.Outputs.Count; i++)
            {
                if (cell.Outputs[i].IsError)
                    items.Add(_dispatcher.AsRenderable(cell.Outputs[i], inputCounter, i, policy));
            }
        }

        // Surface implicit execution errors (no IsError output but ExecutionResult.Failed).
        if (result.Status == ExecutionResult.ExecutionStatus.Failed && cell.Outputs.All(o => !o.IsError))
            items.Add(BuildResultErrorRenderable(result));

        IRenderable body = items.Count == 1 ? items[0] : new Rows(items.ToArray());

        if (_useColor)
        {
            // Emoji glyphs render as double-width in most terminals so they read
            // at a glance; plain ✓/✗ code points are visually tiny in a
            // header. Framed with spaces so the header reads as a tab.
            var glyph = hasError ? "[red bold] ❌ [/]" : "[green bold] ✅ [/]";
            var borderColor = hasError ? Color.Red : Color.Green3;
            var panel = new Panel(body)
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: borderColor),
                Header = new PanelHeader(glyph),
                Padding = new Padding(1, 0, 1, 0),
                Expand = true
            };
            _console.Write(panel);
        }
        else
        {
            _console.Write(body);
            _console.WriteLine();
        }

        MaybePrintElapsed(result, elapsedThreshold);
    }

    private IRenderable BuildResultErrorRenderable(ExecutionResult result)
    {
        var message = result.Error?.Message ?? Strings.Render_ExecutionFailed;
        var name = result.Error?.GetType().Name;
        var stack = result.Error?.StackTrace;

        if (!_useColor)
        {
            var plain = name is { Length: > 0 } ? $"{name}: {message}" : message;
            if (!string.IsNullOrEmpty(stack)) plain += "\n" + stack;
            return new Text(plain);
        }

        var items = new List<IRenderable>();
        var heading = name is { Length: > 0 }
            ? $"[red bold]{Markup.Escape(name)}[/]: {Markup.Escape(message)}"
            : $"[red]{Markup.Escape(message)}[/]";
        items.Add(new Markup(heading));
        if (!string.IsNullOrEmpty(stack))
            items.Add(new Markup($"[dim]{Markup.Escape(stack)}[/]"));
        return new Rows(items.ToArray());
    }

    private void MaybePrintElapsed(ExecutionResult result, TimeSpan threshold)
    {
        if (result.Elapsed < threshold || result.Elapsed <= TimeSpan.Zero) return;
        var elapsed = string.Format(Strings.Render_ExecutedIn, FormatElapsed(result.Elapsed));
        if (_useColor)
            _console.MarkupLine(Messages.In("dim", Markup.Escape(elapsed)));
        else
            _console.WriteLine(elapsed);
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds >= 1) return $"{elapsed.TotalSeconds:0.00} s";
        return $"{elapsed.TotalMilliseconds:0} ms";
    }
}
