using Verso.Abstractions;

namespace Verso.Blazor.Shared.Models;

/// <summary>
/// The notebook's debugging state as a front end sees it at one moment: whether debugging is on,
/// which lines carry a breakpoint, and where a cell is stopped.
/// </summary>
/// <param name="Enabled">Whether debugging is on for the notebook.</param>
/// <param name="Breakpoints">
/// Breakpoint lines by cell, ascending, counting from 1 in the cell's own text. A cell with no
/// breakpoints has no entry.
/// </param>
/// <param name="Stop">
/// Where a cell is stopped, in the cell's own line numbers, or <c>null</c> when nothing is stopped.
/// </param>
public sealed record DebugSnapshot(
    bool Enabled,
    IReadOnlyDictionary<Guid, IReadOnlyList<int>> Breakpoints,
    DebugStop? Stop)
{
    private static readonly IReadOnlyList<int> NoLines = Array.Empty<int>();

    /// <summary>Debugging off, no breakpoints, nothing stopped.</summary>
    public static DebugSnapshot Off { get; } =
        new(false, new Dictionary<Guid, IReadOnlyList<int>>(), null);

    /// <summary>Whether a cell is stopped right now.</summary>
    public bool IsStopped => Stop is not null;

    /// <summary>How many breakpoints are set across the whole notebook.</summary>
    public int BreakpointCount => Breakpoints.Values.Sum(lines => lines.Count);

    /// <summary>The breakpoint lines of one cell, or an empty list when it has none.</summary>
    public IReadOnlyList<int> BreakpointsIn(Guid cellId)
        => Breakpoints.TryGetValue(cellId, out var lines) ? lines : NoLines;

    /// <summary>
    /// The line execution is stopped at when that line is in the given cell, or <c>null</c> when
    /// nothing is stopped or the stop is somewhere else.
    /// </summary>
    public int? StoppedLineIn(Guid cellId)
        => Stop is { Frames.Count: > 0 } stop && stop.Frames[0].CellId == cellId && stop.Frames[0].Line >= 1
            ? stop.Frames[0].Line
            : null;

    /// <summary>
    /// The lines in the given cell that are waiting on a call further down the chain, which is
    /// every frame but the innermost. Empty when nothing is stopped.
    /// </summary>
    public IReadOnlyList<int> CallerLinesIn(Guid cellId)
    {
        if (Stop is null || Stop.Frames.Count < 2)
            return NoLines;

        var stoppedLine = StoppedLineIn(cellId);
        return Stop.Frames
            .Skip(1)
            .Where(frame => frame.CellId == cellId && frame.Line >= 1 && frame.Line != stoppedLine)
            .Select(frame => frame.Line)
            .Distinct()
            .ToArray();
    }
}
