namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The questions every cell asks of the notebook's debugging state. Each cell reads only its own
/// share, so a wrong answer here shows up as a marker in the wrong cell rather than a failure.
/// </summary>
[TestClass]
public sealed class DebugSnapshotTests
{
    private static readonly Guid CellA = Guid.NewGuid();
    private static readonly Guid CellB = Guid.NewGuid();

    private static readonly IReadOnlyList<DebugVariable> NoVariables = Array.Empty<DebugVariable>();

    private static DebugFrame Frame(Guid? cellId, int line, string name = "")
        => new(name, cellId, line, NoVariables);

    private static DebugSnapshot Stopped(params DebugFrame[] frames)
        => new(true, new Dictionary<Guid, IReadOnlyList<int>>(), new DebugStop(DebugStopReason.Breakpoint, frames));

    [TestMethod]
    public void Off_IsDisabledEmptyAndNotStopped()
    {
        var off = DebugSnapshot.Off;

        Assert.IsFalse(off.Enabled);
        Assert.IsFalse(off.IsStopped);
        Assert.IsNull(off.Stop);
        Assert.AreEqual(0, off.BreakpointCount);
        Assert.AreEqual(0, off.BreakpointsIn(CellA).Count);
        Assert.IsNull(off.StoppedLineIn(CellA));
        Assert.AreEqual(0, off.CallerLinesIn(CellA).Count);
    }

    [TestMethod]
    public void BreakpointsIn_CellWithBreakpoints_ReturnsItsLines()
    {
        var state = new DebugSnapshot(true, new Dictionary<Guid, IReadOnlyList<int>>
        {
            [CellA] = new[] { 2, 5 },
        }, null);

        CollectionAssert.AreEqual(new[] { 2, 5 }, state.BreakpointsIn(CellA).ToArray());
    }

    [TestMethod]
    public void BreakpointsIn_CellWithoutBreakpoints_ReturnsEmpty()
    {
        // A cell with no entry is the common case, and it is answered rather than thrown on.
        var state = new DebugSnapshot(true, new Dictionary<Guid, IReadOnlyList<int>>
        {
            [CellA] = new[] { 2 },
        }, null);

        Assert.AreEqual(0, state.BreakpointsIn(CellB).Count);
    }

    [TestMethod]
    public void BreakpointCount_SumsEveryCell()
    {
        var state = new DebugSnapshot(true, new Dictionary<Guid, IReadOnlyList<int>>
        {
            [CellA] = new[] { 1, 4, 9 },
            [CellB] = new[] { 3 },
        }, null);

        Assert.AreEqual(4, state.BreakpointCount);
    }

    [TestMethod]
    public void IsStopped_FollowsStop()
    {
        Assert.IsTrue(Stopped(Frame(CellA, 3)).IsStopped);
        Assert.IsFalse(new DebugSnapshot(true, new Dictionary<Guid, IReadOnlyList<int>>(), null).IsStopped);
    }

    [TestMethod]
    public void StoppedLineIn_AnswersOnlyForTheInnermostFramesCell()
    {
        // Cell B called a function that cell A defined, and the stop is inside that function.
        // The highlight belongs in A; B only waits on the call.
        var state = Stopped(Frame(CellA, 3, "helper"), Frame(CellB, 7));

        Assert.AreEqual(3, state.StoppedLineIn(CellA));
        Assert.IsNull(state.StoppedLineIn(CellB));
    }

    [TestMethod]
    public void StoppedLineIn_NothingStopped_IsNull()
    {
        var state = new DebugSnapshot(true, new Dictionary<Guid, IReadOnlyList<int>>
        {
            [CellA] = new[] { 3 },
        }, null);

        Assert.IsNull(state.StoppedLineIn(CellA));
    }

    [TestMethod]
    public void StoppedLineIn_InnermostFrameWithUnknownLine_IsNull()
    {
        Assert.IsNull(Stopped(Frame(CellA, 0)).StoppedLineIn(CellA));
    }

    [TestMethod]
    public void StoppedLineIn_InnermostFrameOutsideAnyCell_IsNullEverywhere()
    {
        var state = Stopped(Frame(null, 12, "library"), Frame(CellA, 4));

        Assert.IsNull(state.StoppedLineIn(CellA));
    }

    [TestMethod]
    public void CallerLinesIn_ReturnsOuterFramesInThatCell()
    {
        var state = Stopped(Frame(CellA, 3, "inner"), Frame(CellB, 7, "outer"), Frame(CellB, 10));

        CollectionAssert.AreEquivalent(new[] { 7, 10 }, state.CallerLinesIn(CellB).ToArray());
        Assert.AreEqual(0, state.CallerLinesIn(CellA).Count,
            "The innermost frame is the stop itself, not a caller.");
    }

    [TestMethod]
    public void CallerLinesIn_ExcludesTheStoppedLine()
    {
        // A recursive call stops on the same line its caller is waiting on. That line is already
        // marked as the stop, and marking it twice would let the quieter marker hide the louder one.
        var state = Stopped(Frame(CellA, 4, "walk"), Frame(CellA, 4, "walk"), Frame(CellA, 9));

        CollectionAssert.AreEqual(new[] { 9 }, state.CallerLinesIn(CellA).ToArray());
    }

    [TestMethod]
    public void CallerLinesIn_IsDistinct()
    {
        var state = Stopped(Frame(CellA, 2, "f"), Frame(CellB, 6, "g"), Frame(CellB, 6, "g"), Frame(CellB, 11));

        CollectionAssert.AreEqual(new[] { 6, 11 }, state.CallerLinesIn(CellB).ToArray());
    }

    [TestMethod]
    public void CallerLinesIn_IgnoresFramesWithNoCellOrNoLine()
    {
        var state = Stopped(Frame(CellA, 2, "f"), Frame(null, 30, "library"), Frame(CellB, 0, "g"), Frame(CellB, 8));

        CollectionAssert.AreEqual(new[] { 8 }, state.CallerLinesIn(CellB).ToArray());
    }

    [TestMethod]
    public void CallerLinesIn_SingleFrame_IsEmpty()
    {
        Assert.AreEqual(0, Stopped(Frame(CellA, 3)).CallerLinesIn(CellA).Count);
    }
}
