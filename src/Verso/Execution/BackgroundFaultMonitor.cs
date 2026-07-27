using System.Collections.Concurrent;
using Verso.Abstractions;

namespace Verso.Execution;

/// <summary>
/// Holds failures from work a notebook started but did not wait for, until there is a cell to
/// report them against.
/// <para>
/// One sink belongs to one notebook. Faults are attributed to the notebook that was running when
/// they surfaced, which is not always the one that caused them, so the reported text says where
/// they came from.
/// </para>
/// </summary>
public sealed class BackgroundFaultSink
{
    /// <summary>
    /// How many faults one queue holds before it starts discarding the oldest. A notebook whose
    /// background work fails in a loop would otherwise grow this without limit, and the first few
    /// reports say everything the hundredth would.
    /// </summary>
    private const int MaxHeld = 100;

    private readonly ConcurrentQueue<string> _faults = new();
    private int _executing;
    private int _dropped;

    /// <summary>Whether this notebook is inside a cell right now.</summary>
    public bool IsExecuting => Volatile.Read(ref _executing) > 0;

    /// <summary>
    /// Marks this notebook as running a cell for the lifetime of the returned scope. Counted
    /// rather than flagged, because a cell can run while another is still finishing.
    /// </summary>
    public IDisposable BeginExecution()
    {
        Interlocked.Increment(ref _executing);
        return new ExecutionScope(this);
    }

    /// <summary>Records a fault to be reported with this notebook's next cell result.</summary>
    public void Record(string kind, Exception? exception)
    {
        var detail = exception?.ToString() ?? "No exception detail was available.";
        _faults.Enqueue($"{kind}: {detail}");

        // A queue nobody drains, because the notebook it belongs to has gone idle or closed, must
        // not be able to grow for the life of the process. What is dropped is counted, so the
        // report can say the list is partial instead of quietly presenting the tail as the whole.
        while (_faults.Count > MaxHeld && _faults.TryDequeue(out _))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>
    /// Takes everything recorded so far, leaving the queue empty. Returns an empty list when
    /// nothing has gone wrong, which is the usual case.
    /// </summary>
    public IReadOnlyList<CellOutput> Drain()
    {
        if (_faults.IsEmpty)
            return Array.Empty<CellOutput>();

        var drained = new List<CellOutput>();
        while (_faults.TryDequeue(out var fault))
        {
            drained.Add(new CellOutput(
                "text/plain",
                fault,
                IsError: true,
                ErrorName: "BackgroundFailure"));
        }

        // The oldest go first when the cap is reached, and the oldest is often the one that
        // explains the rest, so say how many are missing rather than let the tail speak for them.
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
        {
            drained.Insert(0, new CellOutput(
                "text/plain",
                $"{dropped} earlier background failure(s) were discarded; the {MaxHeld} most recent are shown.",
                IsError: true,
                ErrorName: "BackgroundFailure"));
        }

        return drained;
    }

    private sealed class ExecutionScope : IDisposable
    {
        private readonly BackgroundFaultSink _sink;
        private bool _ended;

        public ExecutionScope(BackgroundFaultSink sink) => _sink = sink;

        public void Dispose()
        {
            if (_ended) return;
            _ended = true;
            Interlocked.Decrement(ref _sink._executing);
        }
    }
}

/// <summary>
/// Catches failures that happen on work a cell started but did not wait for, and routes them to
/// the notebook they most likely belong to.
/// <para>
/// Cell code can start a task or a thread and return before it finishes. When that work then
/// throws, the exception has nowhere to go: the cell has already been answered, so no result can
/// carry it. Left alone, .NET discards an unobserved task exception silently, which is how a
/// notebook can look entirely successful while its work failed.
/// </para>
/// <para>
/// The runtime reports such a failure with no indication of which notebook it came from, and by
/// then the work that raised it is gone. So a fault goes to the one notebook that is inside a cell
/// when it surfaces, which is the only case where the answer is knowable, and otherwise to every
/// open notebook. Reporting it in more places than it belongs is the right way to be wrong: a
/// failure nobody is told about is exactly what this exists to prevent.
/// </para>
/// </summary>
public static class BackgroundFaultMonitor
{
    private static readonly ConcurrentDictionary<BackgroundFaultSink, byte> Sinks = new();

    /// <summary>Holds faults raised while no notebook is open at all.</summary>
    private static readonly BackgroundFaultSink Unattributed = new();

    private static readonly object InstallLock = new();

    private static bool _installed;

    /// <summary>
    /// Starts watching, once per process. Hosts call this during startup; calling it again does
    /// nothing, so every entry point can call it without coordinating.
    /// </summary>
    public static void Install()
    {
        lock (InstallLock)
        {
            if (_installed)
                return;

            _installed = true;

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                // Marking it observed keeps the default behavior (which under some
                // configurations tears the process down) from applying to notebook code.
                e.SetObserved();
                Record("Unhandled exception in background work", e.Exception);
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                // A raw thread that throws takes the process with it and this cannot prevent
                // that. Recording it still helps: the host writes the queue out as it goes down,
                // which is the difference between a mysterious exit and a stack trace.
                Record("Unhandled exception on a background thread", e.ExceptionObject as Exception);
            };
        }
    }

    /// <summary>
    /// Starts reporting faults to a notebook's sink. Disposing the result stops it, which matters
    /// because a fault raised after a notebook closes must not be held for a queue nobody drains.
    /// </summary>
    public static IDisposable Register(BackgroundFaultSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Sinks[sink] = 0;
        return new Registration(sink);
    }

    /// <summary>
    /// Records a fault against whichever notebook it most likely belongs to. See the remarks on
    /// this class for how that is decided.
    /// </summary>
    public static void Record(string kind, Exception? exception)
    {
        var sinks = Sinks.Keys;
        if (sinks.Count == 0)
        {
            Unattributed.Record(kind, exception);
            return;
        }

        BackgroundFaultSink? executing = null;
        foreach (var sink in sinks)
        {
            if (!sink.IsExecuting)
                continue;

            if (executing is not null)
            {
                // More than one notebook is running, so there is nothing to choose between them.
                executing = null;
                break;
            }

            executing = sink;
        }

        if (executing is not null)
        {
            executing.Record(kind, exception);
            return;
        }

        foreach (var sink in sinks)
            sink.Record(kind, exception);
    }

    /// <summary>
    /// Takes the faults raised while no notebook was open. A host with no notebook of its own
    /// calls this to report what it collected.
    /// </summary>
    public static IReadOnlyList<CellOutput> Drain() => Unattributed.Drain();

    /// <summary>
    /// Forgets every registered notebook and anything held for the host. For the tests, which run
    /// in one process and would otherwise inherit each other's notebooks.
    /// </summary>
    internal static void ResetForTesting()
    {
        Sinks.Clear();
        Unattributed.Drain();
    }

    private sealed class Registration : IDisposable
    {
        private readonly BackgroundFaultSink _sink;

        public Registration(BackgroundFaultSink sink) => _sink = sink;

        public void Dispose() => Sinks.TryRemove(_sink, out _);
    }
}
