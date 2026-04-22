namespace Verso.Cli.Repl.Meta;

/// <summary>
/// One REPL meta-command (a <c>.</c>-prefixed directive routed to the CLI, never to a kernel).
/// Implementations live in <c>Verso.Cli.Repl.Meta.Commands</c> and are registered into
/// <see cref="MetaCommandRegistry"/> at loop construction.
/// </summary>
public interface IMetaCommand
{
    /// <summary>Primary invocation name without the leading dot (e.g. "exit", "kernel").</summary>
    string Name { get; }

    /// <summary>Optional aliases without leading dot (e.g. "quit" for ".exit").</summary>
    IReadOnlyList<string> Aliases => Array.Empty<string>();

    /// <summary>Short one-line description shown by <c>.help</c>.</summary>
    string Summary { get; }

    /// <summary>Multi-line detailed help shown by <c>.help &lt;name&gt;</c>.</summary>
    string DetailedHelp { get; }

    /// <summary>
    /// Executes the meta-command. The raw argument string is everything after the command
    /// name, exactly as typed (e.g. for <c>.export --format html -o x.html</c>, argumentText
    /// is <c>--format html -o x.html</c>).
    /// </summary>
    /// <returns><c>true</c> if the REPL should continue; <c>false</c> to terminate the loop.</returns>
    Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct);
}
