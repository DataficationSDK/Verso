using Spectre.Console;
using Verso.Abstractions;
using Verso.Cli.Repl.Meta;
using Verso.Cli.Repl.Meta.Commands;
using Verso.Cli.Repl.Prompt;
using Verso.Cli.Repl.Rendering;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;
using Verso.Execution;

namespace Verso.Cli.Repl;

/// <summary>
/// The read → submit → execute → render cycle. Owns the current
/// <see cref="ReplSession"/>, prompt, renderer, and meta-command registry.
/// Returns an <see cref="ExitCode"/> when the loop terminates via <c>.exit</c>,
/// EOF, or a fatal error.
/// </summary>
public sealed class ReplLoop
{
    private readonly ReplSession _session;
    private readonly IReplPrompt _prompt;
    private readonly TerminalRenderer _renderer;
    private readonly IAnsiConsole _console;
    private readonly MetaCommandRegistry _metaRegistry;
    private readonly bool _useColor;
    private readonly TimeSpan _elapsedThreshold;

    public ReplLoop(
        ReplSession session,
        IReplPrompt prompt,
        TerminalRenderer renderer,
        IAnsiConsole console,
        bool useColor,
        TimeSpan elapsedThreshold)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _useColor = useColor;
        _elapsedThreshold = elapsedThreshold;

        _metaRegistry = CreateDefaultRegistry();
    }

    /// <summary>
    /// The meta-commands a session starts with, in the order <c>.help</c> lists them.
    /// </summary>
    /// <remarks>
    /// Separate from the constructor so the set can be read without a running session behind it,
    /// which is how the tests reach every command's help text.
    /// </remarks>
    public static MetaCommandRegistry CreateDefaultRegistry()
    {
        var registry = new MetaCommandRegistry();
        registry.Register(new HelpMeta());
        registry.Register(new ExitMeta());
        registry.Register(new ClearMeta());
        registry.Register(new ResetMeta());
        registry.Register(new KernelMeta());
        registry.Register(new VarsMeta());
        registry.Register(new ListMeta());
        registry.Register(new ThemeMeta());
        registry.Register(new LayoutMeta());
        registry.Register(new MdMeta());
        registry.Register(new HistoryMeta());
        registry.Register(new RecallMeta());
        registry.Register(new RerunMeta());
        registry.Register(new SetMeta());
        registry.Register(new ViewMeta());
        registry.Register(new SaveMeta());
        registry.Register(new LoadMeta());
        registry.Register(new ConvertMeta());
        registry.Register(new ExportMeta());
        return registry;
    }

    /// <summary>Exposed for tests and future phases to register additional meta-commands.</summary>
    public MetaCommandRegistry MetaRegistry => _metaRegistry;

    /// <summary>What the reader types to see the REPL's commands. The same in every language.</summary>
    private const string HelpCommand = ".help";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var metaContext = new MetaContext(_session, _console, _renderer, _metaRegistry, _useColor);

        while (!ct.IsCancellationRequested)
        {
            var counter = _session.InputCounter + 1;

            ReplInput input;
            var initialText = _session.PendingInitialText;
            _session.PendingInitialText = null;
            try
            {
                input = await _prompt.ReadAsync(counter, _session.ActiveKernelId, initialText, ct);
            }
            catch (OperationCanceledException)
            {
                return Utilities.ExitCodes.Success;
            }

            switch (input.Kind)
            {
                case ReplInputKind.Eof:
                    return Utilities.ExitCodes.Success;
                case ReplInputKind.Cancelled:
                    continue;
            }

            var text = input.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            // Advance the counter only for submissions we actually process.
            _session.NextInputCounter();

            var firstNonWs = text.TrimStart();
            var isMeta = firstNonWs.Length > 0 && firstNonWs[0] == '.';

            if (isMeta)
            {
                // Resolve meta-command vs .code escape.
                var (name, rest) = SplitMetaCommand(text);
                if (string.Equals(name, "code", StringComparison.OrdinalIgnoreCase))
                {
                    // .code one-shot: treat everything after ".code " (plus any following lines)
                    // as a code cell. Useful for F# #r directives or user-typed leading-dot text.
                    await ExecuteCellAsync(rest.TrimStart('\r', '\n'), ct);
                    continue;
                }

                if (!_metaRegistry.TryResolve(name, out var metaCommand))
                {
                    _console.MarkupLine(
                        Messages.In("red", Messages.Say(Strings.Repl_UnknownMetaCommand, name))
                        + " " + Messages.Typed(Strings.Repl_TypeHelpForList, HelpCommand));
                    await _prompt.AddHistoryAsync(text);
                    continue;
                }

                await _prompt.AddHistoryAsync(text);
                bool keepRunning;
                try
                {
                    keepRunning = await metaCommand.ExecuteAsync(rest, metaContext, ct);
                }
                catch (Exception ex)
                {
                    _console.MarkupLine(Messages.In("red",
                        Messages.Say(Strings.Repl_MetaCommandError, name, ex.Message)));
                    continue;
                }
                if (!keepRunning)
                    return Utilities.ExitCodes.Success;
                continue;
            }

            await _prompt.AddHistoryAsync(text);
            await ExecuteCellAsync(text, ct);
        }

        return Utilities.ExitCodes.Success;
    }

    private async Task ExecuteCellAsync(string source, CancellationToken ct)
    {
        var cellType = _session.NextCellTypeOverride ?? "code";
        _session.NextCellTypeOverride = null;

        var cell = new CellModel
        {
            Type = cellType,
            Language = cellType == "code" ? _session.ActiveKernelId : null,
            Source = source
        };

        _session.Notebook.Cells.Add(cell);
        _session.MarkDirty();

        if (cellType == "markdown")
        {
            // Markdown cells do not execute; render the source directly.
            _renderer.RenderCell(_session.InputCounter, cell, ExecutionResult.Success(cell.Id, 0, TimeSpan.Zero), _elapsedThreshold);
            return;
        }

        ExecutionResult result;
        try
        {
            result = await _session.Scaffold.ExecuteCellAsync(cell.Id, ct);
        }
        catch (OperationCanceledException)
        {
            _console.MarkupLine(Messages.In("yellow", Messages.Say(Strings.Repl_Cancelled)));
            return;
        }
        catch (Exception ex)
        {
            _console.MarkupLine(Messages.In("red", Messages.Say(Strings.Repl_ExecutionError, ex.Message)));
            return;
        }

        _renderer.RenderCell(_session.InputCounter, cell, result, _elapsedThreshold);
    }

    private static (string name, string rest) SplitMetaCommand(string text)
    {
        var trimmed = text.TrimStart();
        // Drop the leading dot.
        if (trimmed.Length == 0 || trimmed[0] != '.')
            return ("", "");
        var afterDot = trimmed.Substring(1);

        // Find the first whitespace character (or end of line) to split the name from args.
        var spaceIdx = -1;
        for (int i = 0; i < afterDot.Length; i++)
        {
            if (char.IsWhiteSpace(afterDot[i]))
            {
                spaceIdx = i;
                break;
            }
        }

        if (spaceIdx < 0)
            return (afterDot, "");

        var name = afterDot.Substring(0, spaceIdx);
        var rest = afterDot.Substring(spaceIdx + 1);
        return (name, rest);
    }
}
