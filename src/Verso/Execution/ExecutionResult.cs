namespace Verso.Execution;

/// <summary>
/// Immutable result of a single cell execution.
/// </summary>
public sealed record ExecutionResult
{
    public enum ExecutionStatus { Success, Cancelled, Failed }

    public ExecutionStatus Status { get; init; }
    public Guid CellId { get; init; }
    public int ExecutionCount { get; init; }
    public TimeSpan Elapsed { get; init; }
    public Exception? Error { get; init; }

    private ExecutionResult() { }

    public static ExecutionResult Success(Guid cellId, int executionCount, TimeSpan elapsed)
        => new()
        {
            Status = ExecutionStatus.Success,
            CellId = cellId,
            ExecutionCount = executionCount,
            Elapsed = elapsed
        };

    public static ExecutionResult Cancelled(Guid cellId, int executionCount, TimeSpan elapsed)
        => new()
        {
            Status = ExecutionStatus.Cancelled,
            CellId = cellId,
            ExecutionCount = executionCount,
            Elapsed = elapsed
        };

    public static ExecutionResult Failed(Guid cellId, int executionCount, TimeSpan elapsed, Exception error)
        => new()
        {
            Status = ExecutionStatus.Failed,
            CellId = cellId,
            ExecutionCount = executionCount,
            Elapsed = elapsed,
            Error = error
        };
}
