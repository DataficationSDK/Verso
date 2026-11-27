namespace Verso.Abstractions;

/// <summary>
/// Augmentation interface for language kernels that can pause a running cell at a line, report
/// where it stopped and what is in scope there, and carry on one step at a time. A kernel
/// implements this alongside <see cref="ILanguageKernel"/>. Implementing
/// <see cref="IDebuggableKernel"/> alone is not a valid extension capability.
/// </summary>
/// <remarks>
/// <para>
/// A host offers breakpoints only on cells whose kernel implements this interface, and calls
/// <see cref="DebugAsync"/> in place of <see cref="ILanguageKernel.ExecuteAsync"/> while the user
/// has debugging turned on. With debugging off the kernel is never asked to do anything
/// differently, so whatever a kernel does to make a cell pausable costs nothing in ordinary runs.
/// </para>
/// <para>
/// How a kernel pauses is its own business: it may instrument the code, use tracing its language
/// runtime provides, or drive a debugger of its own. All the host needs is the report made through
/// <see cref="IDebugSession.StopAsync"/> and for the cell to hold still until that call returns.
/// </para>
/// </remarks>
public interface IDebuggableKernel
{
    /// <summary>
    /// Runs a cell so that it can be paused. Apart from pausing, the run must behave as
    /// <see cref="ILanguageKernel.ExecuteAsync"/> does: the same outputs, the same effect on the
    /// kernel's state, and the same response to cancellation.
    /// </summary>
    /// <param name="code">The source code to execute, exactly as it would be given to <see cref="ILanguageKernel.ExecuteAsync"/>.</param>
    /// <param name="context">Execution context for the cell.</param>
    /// <param name="session">
    /// The host's side of the debugging conversation for this run: which lines carry a breakpoint,
    /// and where to report a stop. Valid until the returned task completes.
    /// </param>
    /// <returns>The outputs the cell produced, as for <see cref="ILanguageKernel.ExecuteAsync"/>.</returns>
    Task<IReadOnlyList<CellOutput>> DebugAsync(string code, IExecutionContext context, IDebugSession session);
}
