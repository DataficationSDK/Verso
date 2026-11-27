using System.ComponentModel;

namespace Verso.Kernels.Debugging;

/// <summary>
/// The calls a C# cell makes while it runs with debugging on. The kernel adds them to the cell's
/// code before compiling it, one ahead of each statement, and each asks whether the cell should
/// stop there.
/// </summary>
/// <remarks>
/// Public only because compiled cell code has to reach it. Nothing here is meant to be called by
/// hand, and none of it is a stable interface.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CellProbes
{
    private static readonly object RegisterLock = new();

    // Indexed by the handle compiled into a cell's code. A handle is never reused, because code
    // compiled against it can outlive the kernel that compiled it. Slots are only ever filled
    // and the array only ever replaced by a longer copy, so a running cell reads it without
    // taking the lock.
    private static DebugUnit[] _units = new DebugUnit[64];
    private static int _count;

    // The function the current logical call is in, which is what tells a new frame who called it.
    // An AsyncLocal rather than a thread static so the answer survives an await.
    private static readonly AsyncLocal<CellFrame?> Current = new();

    /// <summary>
    /// Whether the cell should stop at a statement outside any function.
    /// </summary>
    public static bool Hit(int unit, int probe) => Volatile.Read(ref _units)[unit].Root.Hit(probe);

    /// <summary>
    /// Stops the cell at a statement outside any function until the user lets it carry on.
    /// </summary>
    public static void Pause(int unit, int probe, object[] values)
        => Volatile.Read(ref _units)[unit].Root.Pause(probe, values);

    /// <summary>
    /// Marks the start of a function's body. Disposing the result marks its end.
    /// </summary>
    public static CellFrame Enter(int unit, int method)
    {
        var frame = new CellFrame(Volatile.Read(ref _units)[unit], method, Current.Value);
        Current.Value = frame;
        return frame;
    }

    internal static void SetCurrent(CellFrame? frame) => Current.Value = frame;

    internal static int Register(DebugUnit unit)
    {
        lock (RegisterLock)
        {
            var units = _units;
            if (_count == units.Length)
            {
                var grown = new DebugUnit[units.Length * 2];
                Array.Copy(units, grown, units.Length);
                units = grown;
            }

            units[_count] = unit;

            // Published after the slot is filled, so code that learns the handle finds the unit.
            Volatile.Write(ref _units, units);
            return _count++;
        }
    }
}

/// <summary>
/// One running function of a cell being debugged: where it is, and who called it.
/// </summary>
/// <remarks>
/// Public only because compiled cell code holds one in a local. Not a stable interface.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CellFrame : IDisposable
{
    internal CellFrame(DebugUnit unit, int method, CellFrame? parent)
    {
        Unit = unit;
        Method = method;
        Parent = parent;
        Depth = parent is null ? (method < 0 ? 0 : 1) : parent.Depth + 1;
    }

    internal DebugUnit Unit { get; }

    /// <summary>Index into the unit's function names, or -1 for code outside any function.</summary>
    internal int Method { get; }

    internal CellFrame? Parent { get; }

    internal int Depth { get; }

    /// <summary>The statement this frame last reached, which is where it sits while it waits on a call.</summary>
    internal int Probe { get; private set; } = -1;

    /// <summary>
    /// Whether the cell should stop at a statement of this function.
    /// </summary>
    public bool Hit(int probe)
    {
        Probe = probe;
        var unit = Unit;
        var breaks = unit.Breaks;
        return ((uint)probe < (uint)breaks.Length && breaks[probe]) || unit.Controller.IsSteppingTo(this);
    }

    /// <summary>
    /// Stops the cell at a statement of this function until the user lets it carry on.
    /// </summary>
    public void Pause(int probe, object[] values) => Unit.Controller.Pause(this, probe, values);

    /// <summary>
    /// Called before an iterator hands a value to its consumer. The consumer runs next, and the
    /// functions it calls are its own callees rather than this iterator's.
    /// </summary>
    public void Suspend() => CellProbes.SetCurrent(Parent);

    /// <summary>
    /// Called when an iterator is asked for its next value.
    /// </summary>
    public void Resume() => CellProbes.SetCurrent(this);

    /// <inheritdoc />
    public void Dispose() => CellProbes.SetCurrent(Parent);
}

/// <summary>
/// What is known about one statement a cell can stop at.
/// </summary>
/// <param name="Line">The line the statement starts on, counting from 1.</param>
/// <param name="EndLine">
/// The last line a breakpoint may sit on and still mean this statement: its final line for a
/// statement that is a single action, its first for one that holds other statements.
/// </param>
/// <param name="Method">Index of the function the statement is in, or -1 outside any function.</param>
/// <param name="Names">The variables in scope at the statement, in the order their values are captured.</param>
/// <param name="TypeNames">The declared type of each variable, as written or inferred in the cell.</param>
internal sealed record ProbeInfo(int Line, int EndLine, int Method, string[] Names, string[] TypeNames);

/// <summary>
/// One compiled cell that can be paused: its statements, its functions, and which statements
/// carry a breakpoint right now.
/// </summary>
internal sealed class DebugUnit
{
    private bool[] _breaks;
    private CSharpDebugController _controller;

    public DebugUnit(CSharpDebugController controller, Guid cellId, ProbeInfo[] probes, string[] methods)
    {
        _controller = controller;
        CellId = cellId;
        Probes = probes;
        Methods = methods;
        _breaks = new bool[probes.Length];
        Root = new CellFrame(this, method: -1, parent: null);
    }

    public CSharpDebugController Controller => Volatile.Read(ref _controller);
    public Guid CellId { get; }
    public ProbeInfo[] Probes { get; private set; }
    public string[] Methods { get; private set; }

    /// <summary>The frame for the cell's code outside any function.</summary>
    public CellFrame Root { get; }

    /// <summary>
    /// Set once the cell has been run again. The older code can still be running, in a function a
    /// later cell kept hold of, but the breakpoints the user sees belong to the newer text.
    /// </summary>
    public bool Superseded { get; set; }

    /// <summary>One flag per statement. Replaced whole, never written in place.</summary>
    public bool[] Breaks => Volatile.Read(ref _breaks);

    public void SetBreaks(bool[] breaks) => Volatile.Write(ref _breaks, breaks);

    public void ClearBreaks()
    {
        var breaks = Breaks;
        if (Array.IndexOf(breaks, true) >= 0)
            SetBreaks(new bool[breaks.Length]);
    }

    /// <summary>
    /// Cuts the unit loose from the kernel that compiled it. The compiled code may still be
    /// running somewhere and still finds this unit by its handle, but from here on it is never
    /// asked to stop and holds on to nothing of the notebook it came from.
    /// </summary>
    public void Detach()
    {
        Volatile.Write(ref _controller, CSharpDebugController.Inert);
        SetBreaks(Array.Empty<bool>());
        Probes = Array.Empty<ProbeInfo>();
        Methods = Array.Empty<string>();
    }

    /// <summary>
    /// Marks the statements a set of breakpoint lines lands on. A line with a statement starting
    /// on it takes that statement. Otherwise a line inside a statement that spans several takes
    /// the innermost such statement, and any other line takes the next statement below it.
    /// </summary>
    public static bool[] Bind(ProbeInfo[] probes, IReadOnlyCollection<int> lines)
    {
        var breaks = new bool[probes.Length];
        foreach (var line in lines)
        {
            var bound = -1;
            var containing = -1;
            var next = -1;

            for (var i = 0; i < probes.Length; i++)
            {
                var probe = probes[i];
                if (probe.Line == line)
                {
                    bound = i;
                    break;
                }

                if (probe.Line < line && line <= probe.EndLine
                    && (containing < 0 || probe.Line >= probes[containing].Line))
                    containing = i;

                if (probe.Line > line && (next < 0 || probe.Line < probes[next].Line))
                    next = i;
            }

            if (bound < 0) bound = containing >= 0 ? containing : next;
            if (bound >= 0) breaks[bound] = true;
        }
        return breaks;
    }
}
