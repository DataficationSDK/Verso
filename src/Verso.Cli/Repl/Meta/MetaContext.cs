using Spectre.Console;
using Verso.Cli.Repl.Rendering;

namespace Verso.Cli.Repl.Meta;

/// <summary>
/// The dependencies each <see cref="IMetaCommand"/> needs at execution time.
/// Passed by reference so meta-commands can mutate session state
/// (e.g. <c>.kernel csharp</c> updates <see cref="ReplSession.ActiveKernelId"/>).
/// </summary>
public sealed class MetaContext
{
    public ReplSession Session { get; }
    public IAnsiConsole Console { get; }
    public TerminalRenderer Renderer { get; }
    public MetaCommandRegistry Registry { get; }
    public bool UseColor { get; }

    public MetaContext(ReplSession session, IAnsiConsole console, TerminalRenderer renderer, MetaCommandRegistry registry, bool useColor)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Console = console ?? throw new ArgumentNullException(nameof(console));
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        UseColor = useColor;
    }
}
