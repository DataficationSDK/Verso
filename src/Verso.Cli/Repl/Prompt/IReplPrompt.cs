namespace Verso.Cli.Repl.Prompt;

/// <summary>
/// Abstracts over the interactive prompt driver so the REPL loop can be unit-tested
/// without a real terminal and so PrettyPrompt can be swapped for the plain fallback.
/// </summary>
public interface IReplPrompt : IAsyncDisposable
{
    /// <summary>
    /// Reads one submission from the user. Returns <see cref="ReplInputKind.Eof"/>
    /// when stdin closes (Ctrl+D at empty prompt, EOF on pipe), or
    /// <see cref="ReplInputKind.Cancelled"/> when the user clears the buffer mid-edit.
    /// </summary>
    /// <param name="inputCounter">The counter value rendered in the prompt (e.g. [3]).</param>
    /// <param name="activeKernelId">The active kernel, passed to language-specific services.</param>
    /// <param name="initialText">Optional seed text (used by .recall).</param>
    /// <param name="ct">Cancellation for the read operation.</param>
    Task<ReplInput> ReadAsync(int inputCounter, string? activeKernelId, string? initialText, CancellationToken ct);

    /// <summary>
    /// Appends the submission to the persistent prompt history, if history is enabled.
    /// </summary>
    Task AddHistoryAsync(string submission);
}
