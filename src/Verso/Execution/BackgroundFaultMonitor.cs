using System.Collections.Concurrent;
using Verso.Abstractions;

namespace Verso.Execution;

/// <summary>
/// Catches failures that happen on work a cell started but did not wait for, and holds them until
/// there is a cell to report them against.
/// <para>
/// Cell code can start a task or a thread and return before it finishes. When that work then
/// throws, the exception has nowhere to go: the cell has already been answered, so no result can
/// carry it. Left alone, .NET discards an unobserved task exception silently, which is how a
/// notebook can look entirely successful while its work failed.
/// </para>
/// <para>
/// Faults are attributed to the cell that is running when they surface, or to the next one to run.
/// That is not always the cell that caused them, so the text says where they came from.
/// </para>
/// </summary>
public static class BackgroundFaultMonitor
{
    private static readonly ConcurrentQueue<string> Faults = new();
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

    /// <summary>Records a fault to be reported with the next cell result.</summary>
    public static void Record(string kind, Exception? exception)
    {
        var detail = exception?.ToString() ?? "No exception detail was available.";
        Faults.Enqueue($"{kind}: {detail}");
    }

    /// <summary>
    /// Takes everything recorded so far, leaving the queue empty. Returns an empty list when
    /// nothing has gone wrong, which is the usual case.
    /// </summary>
    public static IReadOnlyList<CellOutput> Drain()
    {
        if (Faults.IsEmpty)
            return Array.Empty<CellOutput>();

        var drained = new List<CellOutput>();
        while (Faults.TryDequeue(out var fault))
        {
            drained.Add(new CellOutput(
                "text/plain",
                fault,
                IsError: true,
                ErrorName: "BackgroundFailure"));
        }

        return drained;
    }
}
