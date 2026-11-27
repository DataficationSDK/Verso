using Verso.Abstractions;

namespace Verso.Debugging;

/// <summary>
/// One notebook's debugging state: whether debugging is on, which lines carry a breakpoint, and
/// where a cell is stopped right now. A front end drives it, and a kernel that implements
/// <see cref="IDebuggableKernel"/> hears about it through the <see cref="IDebugSession"/> each run
/// is given.
/// </summary>
/// <remarks>
/// Breakpoints and reported locations are kept here in the cell's own line numbers, the ones the
/// user sees in the editor. The session handed to a kernel speaks in lines of the code that kernel
/// was given, which can start further down the cell, and the translation between the two is done
/// here. Breakpoints belong to the session and are not written into the notebook.
/// </remarks>
public sealed class NotebookDebugger
{
    private static readonly IReadOnlyList<int> NoLines = Array.Empty<int>();

    private readonly object _lock = new();
    private readonly Dictionary<Guid, SortedSet<int>> _breakpoints = new();

    // How many lines of each cell came before the code its kernel was given, as of that cell's
    // most recent debug run.
    private readonly Dictionary<Guid, int> _lineOffsets = new();

    private readonly List<RunSession> _sessions = new();

    // One stop is shown at a time. A second thread reaching a breakpoint waits its turn here.
    private readonly SemaphoreSlim _stopGate = new(1, 1);

    private bool _enabled;
    private DebugStop? _currentStop;
    private TaskCompletionSource<DebugResumeAction>? _pendingResume;

    /// <summary>
    /// Raised when debugging is turned on or off, a breakpoint changes, or a cell stops or carries
    /// on. May be raised on any thread.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Whether cells run through <see cref="IDebuggableKernel.DebugAsync"/>. Turning this off
    /// lets a stopped cell run on to its end.
    /// </summary>
    public bool Enabled
    {
        get { lock (_lock) return _enabled; }
        set
        {
            TaskCompletionSource<DebugResumeAction>? release = null;
            lock (_lock)
            {
                if (_enabled == value) return;
                _enabled = value;

                if (!value)
                {
                    release = _pendingResume;
                    _pendingResume = null;
                    _currentStop = null;
                }
            }

            // Breakpoints read as empty while debugging is off, so kernels are told they changed.
            NotifyBreakpointsChanged();
            release?.TrySetResult(DebugResumeAction.Continue);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Where a cell is stopped, or <c>null</c> when nothing is stopped. This is the kernel's own
    /// report with its lines moved into the cell's numbering, the one the user sees.
    /// </summary>
    public DebugStop? CurrentStop
    {
        get { lock (_lock) return _currentStop; }
    }

    /// <summary>
    /// The breakpoint lines of a cell, ascending, counting from 1 in the cell's own text.
    /// </summary>
    public IReadOnlyList<int> GetBreakpoints(Guid cellId)
    {
        lock (_lock)
        {
            return _breakpoints.TryGetValue(cellId, out var lines) ? lines.ToArray() : NoLines;
        }
    }

    /// <summary>
    /// Every cell that has a breakpoint, with its lines ascending.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<int>> GetAllBreakpoints()
    {
        lock (_lock)
        {
            return _breakpoints.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<int>)pair.Value.ToArray());
        }
    }

    /// <summary>
    /// Replaces the breakpoints of a cell. Lines below 1 are ignored, and an empty set clears the cell.
    /// </summary>
    public void SetBreakpoints(Guid cellId, IEnumerable<int> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var requested = new SortedSet<int>(lines.Where(line => line >= 1));
        lock (_lock)
        {
            var had = _breakpoints.TryGetValue(cellId, out var existing);
            if (had ? existing!.SetEquals(requested) : requested.Count == 0)
                return;

            if (requested.Count == 0)
                _breakpoints.Remove(cellId);
            else
                _breakpoints[cellId] = requested;
        }

        NotifyBreakpointsChanged();
        Changed?.Invoke();
    }

    /// <summary>
    /// Forgets everything held about a cell that no longer exists.
    /// </summary>
    public void ForgetCell(Guid cellId)
    {
        bool hadBreakpoints;
        lock (_lock)
        {
            hadBreakpoints = _breakpoints.Remove(cellId);
            _lineOffsets.Remove(cellId);
        }

        if (!hadBreakpoints) return;
        NotifyBreakpointsChanged();
        Changed?.Invoke();
    }

    /// <summary>
    /// Lets a stopped cell carry on.
    /// </summary>
    /// <returns><c>false</c> when nothing was stopped.</returns>
    public bool Resume(DebugResumeAction action)
    {
        TaskCompletionSource<DebugResumeAction>? pending;
        lock (_lock)
        {
            pending = _pendingResume;
            if (pending is null) return false;
            _pendingResume = null;
            _currentStop = null;
        }

        pending.TrySetResult(action);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Starts the debugging conversation for one run of a cell.
    /// </summary>
    /// <param name="cellId">The cell being run.</param>
    /// <param name="lineOffset">How many lines of the cell come before the code the kernel is given.</param>
    /// <param name="cancellationToken">The run's own token. Cancelling it ends a stop.</param>
    internal RunSession BeginRun(Guid cellId, int lineOffset, CancellationToken cancellationToken)
    {
        var session = new RunSession(this, cancellationToken);
        lock (_lock)
        {
            _lineOffsets[cellId] = Math.Max(0, lineOffset);
            _sessions.Add(session);
        }
        return session;
    }

    private void EndRun(RunSession session)
    {
        lock (_lock) _sessions.Remove(session);
    }

    private void NotifyBreakpointsChanged()
    {
        RunSession[] sessions;
        lock (_lock) sessions = _sessions.ToArray();
        foreach (var session in sessions)
            session.RaiseBreakpointsChanged();
    }

    private IReadOnlyCollection<int> GetKernelBreakpoints(Guid cellId)
    {
        lock (_lock)
        {
            if (!_enabled || !_breakpoints.TryGetValue(cellId, out var lines))
                return NoLines;

            var offset = _lineOffsets.GetValueOrDefault(cellId);
            return lines.Select(line => line - offset).Where(line => line >= 1).ToArray();
        }
    }

    private async Task<DebugResumeAction> StopAsync(
        DebugStop stop, CancellationToken runToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stop);
        if (stop.Frames.Count == 0)
            throw new ArgumentException("A stop must report at least one frame.", nameof(stop));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(runToken, cancellationToken);
        var token = linked.Token;

        await _stopGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var resume = new TaskCompletionSource<DebugResumeAction>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock)
            {
                // Debugging was turned off between the kernel deciding to stop and getting here.
                if (!_enabled)
                    return DebugResumeAction.Continue;

                _currentStop = new DebugStop(stop.Reason, stop.Frames.Select(ToCellLines).ToArray());
                _pendingResume = resume;
            }

            Changed?.Invoke();

            using var registration = token.Register(() => resume.TrySetCanceled(token));
            try
            {
                return await resume.Task.ConfigureAwait(false);
            }
            finally
            {
                bool wasShowing;
                lock (_lock)
                {
                    wasShowing = ReferenceEquals(_pendingResume, resume);
                    if (wasShowing)
                    {
                        _pendingResume = null;
                        _currentStop = null;
                    }
                }

                // Resume and turning debugging off have already announced the change.
                if (wasShowing)
                    Changed?.Invoke();
            }
        }
        finally
        {
            _stopGate.Release();
        }
    }

    // Callers hold _lock.
    private DebugFrame ToCellLines(DebugFrame frame)
    {
        if (frame.CellId is not { } cellId || frame.Line < 1)
            return frame;

        var offset = _lineOffsets.GetValueOrDefault(cellId);
        return offset == 0 ? frame : frame with { Line = frame.Line + offset };
    }

    /// <summary>
    /// What one run of a cell sees of the debugger.
    /// </summary>
    internal sealed class RunSession : IDebugSession, IDisposable
    {
        private readonly NotebookDebugger _owner;
        private readonly CancellationToken _runToken;

        public RunSession(NotebookDebugger owner, CancellationToken runToken)
        {
            _owner = owner;
            _runToken = runToken;
        }

        public event Action? BreakpointsChanged;

        public IReadOnlyCollection<int> GetBreakpoints(Guid cellId) => _owner.GetKernelBreakpoints(cellId);

        public Task<DebugResumeAction> StopAsync(DebugStop stop, CancellationToken cancellationToken = default)
            => _owner.StopAsync(stop, _runToken, cancellationToken);

        internal void RaiseBreakpointsChanged() => BreakpointsChanged?.Invoke();

        public void Dispose()
        {
            BreakpointsChanged = null;
            _owner.EndRun(this);
        }
    }
}
