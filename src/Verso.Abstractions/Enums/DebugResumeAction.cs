namespace Verso.Abstractions;

/// <summary>
/// How the user asked a stopped cell to carry on.
/// </summary>
public enum DebugResumeAction
{
    /// <summary>
    /// Run until the next breakpoint or the end of the cell.
    /// </summary>
    Continue,

    /// <summary>
    /// Run to the next line in the current function, without stopping inside anything that line calls.
    /// </summary>
    StepOver,

    /// <summary>
    /// Run to the next line, stopping inside a function the current line calls when the kernel can.
    /// </summary>
    StepInto,

    /// <summary>
    /// Run until the current function returns to its caller.
    /// </summary>
    StepOut
}
