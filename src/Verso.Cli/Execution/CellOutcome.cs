using Verso.Abstractions;
using Verso.Execution;

namespace Verso.Cli.Execution;

/// <summary>
/// Whether an executed cell counts as a failure.
/// <para>
/// A kernel that catches an exception and reports it as an error output returns
/// <see cref="ExecutionResult.ExecutionStatus.Success"/>, because the execution itself completed.
/// That is the usual shape of a failing cell: a Python traceback or a C# exception arrives as an
/// error output, not as a failed result. Reading the status alone therefore counts a notebook full
/// of tracebacks as entirely successful, so the outputs have to be consulted too.
/// </para>
/// <para>
/// Everything that reports on a run asks here, so the exit code, the printed summary, and the JSON
/// cannot drift apart again.
/// </para>
/// </summary>
internal static class CellOutcome
{
    /// <summary>
    /// Whether text a kernel wrote to standard error should also count as a failure, set by
    /// <c>--fail-on-stderr</c>. Off by default, because a great deal of ordinary output arrives
    /// there from programs that are succeeding: progress bars, logging, and deprecation warnings.
    /// A pipeline that wants to catch those can turn it on.
    /// <para>
    /// Held here rather than threaded through every caller so that the exit code, the printed
    /// summary, and the JSON cannot disagree about it, which is the same reason this class exists.
    /// </para>
    /// </summary>
    public static bool FailOnStandardError { get; set; }

    /// <summary>
    /// Whether this cell failed. A cell with no result was never executed, which is neither a
    /// success nor a failure.
    /// </summary>
    public static bool Failed(CellModel? cell, ExecutionResult? result)
    {
        if (result is null)
            return false;

        if (result.Status == ExecutionResult.ExecutionStatus.Failed)
            return true;

        return HasErrorOutput(cell);
    }

    /// <summary>Whether this cell ran and did not fail. Cancelled counts as neither.</summary>
    public static bool Succeeded(CellModel? cell, ExecutionResult? result)
        => result is { Status: ExecutionResult.ExecutionStatus.Success } && !HasErrorOutput(cell);

    /// <summary>Whether any executed cell failed.</summary>
    public static bool AnyFailed(
        IReadOnlyList<CellModel> cells, IReadOnlyList<ExecutionResult> results)
    {
        foreach (var result in results)
        {
            if (Failed(Find(cells, result.CellId), result))
                return true;
        }

        return false;
    }

    /// <summary>The cell a result belongs to, or null when it is not among those given.</summary>
    public static CellModel? Find(IReadOnlyList<CellModel> cells, Guid cellId)
    {
        foreach (var cell in cells)
        {
            if (cell.Id == cellId)
                return cell;
        }

        return null;
    }

    private static bool HasErrorOutput(CellModel? cell)
    {
        if (cell is null)
            return false;

        foreach (var output in cell.Outputs)
        {
            if (output.IsError)
                return true;

            if (FailOnStandardError && output.Channel == OutputChannel.Stderr)
                return true;
        }

        return false;
    }
}
