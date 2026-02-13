namespace Verso.Abstractions;

/// <summary>
/// Defines a magic command that can be invoked with a prefix such as <c>%time</c> or <c>#!who</c>.
/// Magic commands provide lightweight, inline directives that extend kernel functionality
/// without requiring a full extension UI.
/// </summary>
public interface IMagicCommand : IExtension
{
    /// <summary>
    /// The command name used to invoke this magic (e.g. "time", "who").
    /// This shadows <see cref="IExtension.Name"/> to provide the invocable command name.
    /// </summary>
    new string Name { get; }

    /// <summary>
    /// Short description of what the command does, displayed in help listings.
    /// This shadows <see cref="IExtension.Description"/> to provide command-specific help text.
    /// </summary>
    new string Description { get; }

    /// <summary>
    /// Definitions of the parameters this command accepts, used for parsing and help generation.
    /// </summary>
    IReadOnlyList<ParameterDefinition> Parameters { get; }

    /// <summary>
    /// Executes the magic command with the provided argument string.
    /// </summary>
    /// <param name="arguments">Raw argument text following the command name.</param>
    /// <param name="context">Context providing access to the kernel, outputs, and notebook state.</param>
    /// <returns>A task that completes when the command has finished executing.</returns>
    Task ExecuteAsync(string arguments, IMagicCommandContext context);
}
