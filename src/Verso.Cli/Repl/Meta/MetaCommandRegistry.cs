namespace Verso.Cli.Repl.Meta;

/// <summary>
/// Name-to-<see cref="IMetaCommand"/> lookup. Aliases and primary names share
/// a case-insensitive table. Third parties do not register meta-commands in v1.0;
/// the table is populated once by <see cref="Verso.Cli.Repl.ReplLoop"/> at startup.
/// </summary>
public sealed class MetaCommandRegistry
{
    private readonly Dictionary<string, IMetaCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IMetaCommand> _ordered = new();

    public void Register(IMetaCommand command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        _commands[command.Name] = command;
        foreach (var alias in command.Aliases)
            _commands[alias] = command;
        _ordered.Add(command);
    }

    public bool TryResolve(string name, out IMetaCommand command)
    {
        if (_commands.TryGetValue(name, out var found))
        {
            command = found;
            return true;
        }
        command = null!;
        return false;
    }

    public IReadOnlyList<IMetaCommand> AllOrdered => _ordered;
}
