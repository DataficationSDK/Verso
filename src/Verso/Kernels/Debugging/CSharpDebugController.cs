using System.Collections;
using System.Globalization;
using Verso.Abstractions;

namespace Verso.Kernels.Debugging;

/// <summary>
/// One C# kernel's debugging state: the cells it has compiled to be pausable, the run that is
/// being debugged now, and the step the user last asked for.
/// </summary>
internal sealed class CSharpDebugController
{
    private const int Running = 0;
    private const int SteppingInto = 1;
    private const int SteppingOver = 2;
    private const int SteppingOut = 3;

    private const int MaxValueLength = 200;

    /// <summary>
    /// Stands in for the controller of a kernel that has been disposed. It never has a run, so
    /// code still calling into it is never stopped.
    /// </summary>
    internal static readonly CSharpDebugController Inert = new();

    // Set while a stop is being described. Describing a value can run code from the cell (a
    // ToString the cell wrote, say) and that code must not try to stop in turn.
    [ThreadStatic] private static bool t_describing;

    private readonly object _unitsLock = new();
    private readonly List<DebugUnit> _units = new();

    // Every unit this kernel ever created, adopted or not, so all of them can be cut loose.
    private readonly List<DebugUnit> _created = new();

    // Held for as long as a cell is stopped, so a second thread reaching a breakpoint waits here
    // rather than running on past it.
    private readonly object _pauseLock = new();

    private IDebugSession? _session;
    private CancellationToken _runToken;

    private volatile int _mode;
    private int _stepDepth;

    /// <summary>
    /// Describes a cell that is about to be compiled with probes, and hands back the handle its
    /// code uses to find that description. The handle has to exist before the code that mentions
    /// it can be written, so this comes first and <see cref="Adopt"/> follows once the code is
    /// known to compile.
    /// </summary>
    public DebugUnit CreateUnit(Guid cellId, ProbeInfo[] probes, string[] methods, out int handle)
    {
        var unit = new DebugUnit(this, cellId, probes, methods);
        lock (_unitsLock) _created.Add(unit);
        handle = CellProbes.Register(unit);
        return unit;
    }

    /// <summary>
    /// Takes on a cell that is about to run. An earlier compilation of the same cell stops taking
    /// breakpoints, since the lines the user sees now belong to the newer text.
    /// </summary>
    public void Adopt(DebugUnit unit)
    {
        lock (_unitsLock)
        {
            foreach (var earlier in _units)
            {
                if (earlier.CellId != unit.CellId || earlier.Superseded) continue;
                earlier.Superseded = true;
                earlier.ClearBreaks();
            }
            _units.Add(unit);
        }

        RebindBreakpoints();
    }

    /// <summary>
    /// Starts a debugged run. Breakpoints are read now and again whenever they change.
    /// </summary>
    public void BeginRun(IDebugSession session, IExecutionContext context)
    {
        _runToken = context.CancellationToken;
        _mode = Running;
        _session = session;
        session.BreakpointsChanged += RebindBreakpoints;
        RebindBreakpoints();
    }

    public void EndRun()
    {
        var session = _session;
        _session = null;
        _mode = Running;
        if (session is not null)
            session.BreakpointsChanged -= RebindBreakpoints;
    }

    /// <summary>
    /// Called when a cell runs with debugging off, so functions compiled earlier with probes stop
    /// asking to pause.
    /// </summary>
    public void ClearBreakpoints()
    {
        lock (_unitsLock)
        {
            foreach (var unit in _units)
                unit.ClearBreaks();
        }
    }

    /// <summary>
    /// Called when the kernel is disposed. Code compiled earlier can outlive it on a thread of its
    /// own, and from here on it runs straight through.
    /// </summary>
    public void Release()
    {
        EndRun();
        lock (_unitsLock)
        {
            foreach (var unit in _created)
                unit.Detach();
            _created.Clear();
            _units.Clear();
        }
    }

    private void RebindBreakpoints()
    {
        var session = _session;
        if (session is null) return;

        lock (_unitsLock)
        {
            foreach (var unit in _units)
            {
                if (unit.Superseded) continue;
                unit.SetBreaks(DebugUnit.Bind(unit.Probes, session.GetBreakpoints(unit.CellId)));
            }
        }
    }

    /// <summary>
    /// Whether a step in progress ends at this frame's next statement.
    /// </summary>
    internal bool IsSteppingTo(CellFrame frame)
    {
        return _mode switch
        {
            Running => false,
            SteppingInto => true,
            SteppingOver => frame.Depth <= _stepDepth,
            _ => frame.Depth < _stepDepth,
        };
    }

    internal void Pause(CellFrame frame, int probe, object[] values)
    {
        if (_session is null || t_describing) return;

        lock (_pauseLock)
        {
            // Read again under the lock: the run may have ended, or another thread's stop may have
            // changed what the user asked for, while this one waited.
            var session = _session;
            if (session is null) return;

            var unit = frame.Unit;
            var breaks = unit.Breaks;
            var stepping = IsSteppingTo(frame);
            if (!stepping && !((uint)probe < (uint)breaks.Length && breaks[probe])) return;
            if ((uint)probe >= (uint)unit.Probes.Length) return;

            _mode = Running;

            DebugStop stop;
            t_describing = true;
            try
            {
                stop = new DebugStop(
                    stepping ? DebugStopReason.Step : DebugStopReason.Breakpoint,
                    DescribeFrames(frame, probe, values));
            }
            finally
            {
                t_describing = false;
            }

            // The cell's own thread waits here. It has nothing else to do until the user decides,
            // and blocking is what keeps the rest of the cell from running.
            var action = session.StopAsync(stop, _runToken).GetAwaiter().GetResult();

            _stepDepth = frame.Depth;
            _mode = action switch
            {
                DebugResumeAction.StepInto => SteppingInto,
                DebugResumeAction.StepOver => SteppingOver,
                DebugResumeAction.StepOut => SteppingOut,
                _ => Running,
            };
        }
    }

    private List<DebugFrame> DescribeFrames(CellFrame frame, int probe, object[] values)
    {
        var frames = new List<DebugFrame>
        {
            new(NameOf(frame), frame.Unit.CellId, frame.Unit.Probes[probe].Line,
                DescribeVariables(frame.Unit.Probes[probe], values)),
        };

        // Callers are known by where they were when they made the call. Their variables are not
        // captured: only the statement that stopped handed its values over.
        for (var caller = frame.Parent; caller is not null; caller = caller.Parent)
        {
            var probes = caller.Unit.Probes;
            var line = (uint)caller.Probe < (uint)probes.Length ? probes[caller.Probe].Line : 0;
            frames.Add(new DebugFrame(NameOf(caller), caller.Unit.CellId, line, Array.Empty<DebugVariable>()));
        }

        return frames;
    }

    private static string NameOf(CellFrame frame)
    {
        var methods = frame.Unit.Methods;
        return (uint)frame.Method < (uint)methods.Length ? methods[frame.Method] : "";
    }

    private static List<DebugVariable> DescribeVariables(ProbeInfo probe, object[] values)
    {
        // The type shown is the declared one, as written or inferred in the cell. It reads the way
        // the code does (int[] rather than Int32[]) and is known even when the value is null.
        var variables = new List<DebugVariable>(probe.Names.Length);
        for (var i = 0; i < probe.Names.Length && i < values.Length; i++)
            variables.Add(new DebugVariable(probe.Names[i], probe.TypeNames[i], DescribeValue(values[i])));
        return variables;
    }

    /// <summary>
    /// A short rendering of a value that leaves the value as it found it. A sequence that has not
    /// been run yet is named rather than counted, because counting it would run it, and for a
    /// sequence that can only be read once that would take its contents away from the cell.
    /// </summary>
    internal static string DescribeValue(object? value)
    {
        if (value is null) return "null";

        try
        {
            var text = value switch
            {
                string s => "\"" + s + "\"",
                bool b => b ? "true" : "false",
                char c => "'" + c + "'",
                IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
                ICollection collection => "Count = " + collection.Count.ToString(CultureInfo.CurrentCulture),
                IEnumerable => "{" + value.GetType().Name + "}",
                Delegate => "{" + value.GetType().Name + "}",
                _ => value.ToString() ?? "",
            };

            return text.Length > MaxValueLength ? text[..(MaxValueLength - 3)] + "..." : text;
        }
        catch
        {
            return "{" + value.GetType().Name + "}";
        }
    }
}
