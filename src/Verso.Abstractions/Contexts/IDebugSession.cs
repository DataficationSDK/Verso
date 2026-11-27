namespace Verso.Abstractions;

/// <summary>
/// The host's side of a debugging run, handed to <see cref="IDebuggableKernel.DebugAsync"/>. It
/// tells the kernel which lines carry a breakpoint and is where the kernel reports that the cell
/// has stopped.
/// </summary>
/// <remarks>
/// <para>
/// Every line number here counts from 1 and refers to the code string the kernel was given for the
/// cell in question, on that cell's most recent run. A host may show the user a cell whose text
/// differs from what the kernel received (lines the host consumed before the kernel saw the code,
/// for instance) and translates between the two, so a kernel never has to.
/// </para>
/// <para>
/// A kernel that can stop in code belonging to an earlier cell, such as a function that cell
/// defined, identifies the location by that cell's <see cref="IExecutionContext.CellId"/>, which
/// it saw when the cell ran.
/// </para>
/// </remarks>
public interface IDebugSession
{
    /// <summary>
    /// The lines of a cell that carry a breakpoint.
    /// </summary>
    /// <param name="cellId">The cell to ask about. Need not be the cell that is running.</param>
    /// <returns>The breakpoint lines, in no particular order. Empty when the cell has none.</returns>
    IReadOnlyCollection<int> GetBreakpoints(Guid cellId);

    /// <summary>
    /// Raised when a breakpoint is added or removed in any cell, which can happen while a cell is
    /// running or stopped. May be raised on any thread.
    /// </summary>
    event Action? BreakpointsChanged;

    /// <summary>
    /// Reports that the cell has stopped and waits for the user to decide how it should carry on.
    /// The kernel must not run any more of the cell until the returned task completes.
    /// </summary>
    /// <param name="stop">Where the cell stopped and what is in scope there.</param>
    /// <param name="cancellationToken">
    /// Cancels the wait. The wait also ends when the cell's own
    /// <see cref="IVersoContext.CancellationToken"/> is cancelled, which is how the user ends a
    /// stopped cell.
    /// </param>
    /// <returns>How the user asked the cell to carry on.</returns>
    /// <exception cref="OperationCanceledException">The cell was cancelled while stopped.</exception>
    Task<DebugResumeAction> StopAsync(DebugStop stop, CancellationToken cancellationToken = default);
}
