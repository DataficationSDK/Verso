namespace Verso.Abstractions;

/// <summary>
/// Context provided to language kernels during cell code execution. Extends <see cref="IVersoContext"/> with execution-specific state.
/// </summary>
public interface IExecutionContext : IVersoContext
{
    /// <summary>
    /// Gets the unique identifier of the cell being executed.
    /// </summary>
    Guid CellId { get; }

    /// <summary>
    /// Gets the monotonically increasing execution counter for the current cell.
    /// </summary>
    int ExecutionCount { get; }

    /// <summary>
    /// Sends a display output that can be updated in place during execution.
    /// </summary>
    /// <param name="output">The cell output to display.</param>
    /// <returns>A task that completes when the output has been displayed.</returns>
    Task DisplayAsync(CellOutput output);
}
