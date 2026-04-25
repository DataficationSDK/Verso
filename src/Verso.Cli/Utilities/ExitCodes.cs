namespace Verso.Cli.Utilities;

/// <summary>
/// Process exit codes matching the Verso CLI specification.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int CellFailure = 1;
    public const int Timeout = 2;
    public const int FileNotFound = 3;
    public const int SerializationError = 4;
    public const int MissingParameters = 5;

    /// <summary>
    /// Resolution failure: a named CLI resource (kernel, theme, layout) could not be
    /// matched against a registered extension. Used by <c>verso repl</c>.
    /// </summary>
    public const int ResolutionFailure = 6;

    /// <summary>
    /// Terminated by SIGINT (two Ctrl+C presses within the cancel window). POSIX convention.
    /// </summary>
    public const int SigInt = 130;

    /// <summary>
    /// Terminated by SIGTERM. POSIX convention.
    /// </summary>
    public const int SigTerm = 143;
}
