namespace Verso.Abstractions;

/// <summary>
/// Context provided to magic command handlers during execution. Extends <see cref="IVersoContext"/> with command-specific state.
/// </summary>
public interface IMagicCommandContext : IVersoContext
{
    /// <summary>
    /// Gets the cell source code that follows the magic command directive.
    /// </summary>
    string RemainingCode { get; }

    /// <summary>
    /// Gets or sets a value indicating whether normal kernel execution should be suppressed after the magic command completes.
    /// </summary>
    bool SuppressExecution { get; set; }
}
