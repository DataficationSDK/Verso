namespace Verso.Abstractions;

/// <summary>
/// Why a cell stopped while being debugged.
/// </summary>
public enum DebugStopReason
{
    /// <summary>
    /// Execution reached a line that carries a breakpoint.
    /// </summary>
    Breakpoint,

    /// <summary>
    /// A step the user asked for has finished.
    /// </summary>
    Step
}
