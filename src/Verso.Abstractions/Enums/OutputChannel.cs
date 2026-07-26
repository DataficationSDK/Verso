namespace Verso.Abstractions;

/// <summary>
/// Identifies which of a kernel's text streams an output arrived on. This records where the text
/// came from, not whether anything went wrong: a great many programs write progress bars, logging,
/// and warnings to standard error while succeeding, so a channel is never on its own a reason to
/// treat a cell as failed. Use <see cref="CellOutput.IsError"/> for that.
/// </summary>
public enum OutputChannel
{
    /// <summary>The kernel's standard output stream.</summary>
    Stdout,

    /// <summary>
    /// The kernel's standard error stream. Rendered so it can be told apart from ordinary output,
    /// but not as a failure.
    /// </summary>
    Stderr
}
