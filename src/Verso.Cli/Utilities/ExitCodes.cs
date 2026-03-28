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
}
