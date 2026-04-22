namespace Verso.Cli.Repl.Prompt;

/// <summary>
/// Classifies what the prompt produced in one read cycle.
/// </summary>
public enum ReplInputKind
{
    /// <summary>User submitted text to be executed or dispatched as a meta-command.</summary>
    Submission,
    /// <summary>User requested exit via Ctrl+D at an empty prompt.</summary>
    Eof,
    /// <summary>User cancelled the current input (Ctrl+C mid-editing); main loop should re-prompt.</summary>
    Cancelled
}

/// <summary>
/// One read cycle's result.
/// </summary>
public sealed record ReplInput(ReplInputKind Kind, string Text)
{
    public static ReplInput Submission(string text) => new(ReplInputKind.Submission, text);
    public static readonly ReplInput Eof = new(ReplInputKind.Eof, "");
    public static readonly ReplInput Cancelled = new(ReplInputKind.Cancelled, "");
}
