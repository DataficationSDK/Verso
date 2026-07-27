using Verso.Execution;

namespace Verso.Tests.Execution;

/// <summary>
/// Cell code can start work and return before that work finishes. When it then throws, there is no
/// longer a result to carry the failure, and .NET discards an unobserved task exception silently.
/// These cover the per-notebook queue that holds such failures until there is a cell to report them
/// against, and the rules deciding which notebook a fault belongs to.
/// </summary>
[TestClass]
public sealed class BackgroundFaultMonitorTests
{
    [TestInitialize]
    public void Setup()
    {
        // The registry is process-wide, and a notebook another test built and did not dispose
        // would otherwise still be listening when these run.
        BackgroundFaultMonitor.ResetForTesting();
    }

    [TestCleanup]
    public void Cleanup()
    {
        BackgroundFaultMonitor.ResetForTesting();
    }

    // --- the queue itself ---

    [TestMethod]
    public void Drain_WithNothingRecorded_ReturnsEmpty()
    {
        Assert.AreEqual(0, new BackgroundFaultSink().Drain().Count);
    }

    [TestMethod]
    public void Drain_ReportsARecordedFaultAsAFailure()
    {
        var sink = new BackgroundFaultSink();
        sink.Record("Unhandled exception in background work", new InvalidOperationException("late failure"));

        var outputs = sink.Drain();

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].IsError, "a background failure has to fail the cell it lands on");
        Assert.AreEqual("BackgroundFailure", outputs[0].ErrorName);
        StringAssert.Contains(outputs[0].Content, "late failure");

        // The text says where it came from, because the cell it is attached to may not be the
        // cell that caused it.
        StringAssert.Contains(outputs[0].Content, "background work");
    }

    [TestMethod]
    public void Drain_EmptiesTheQueue()
    {
        var sink = new BackgroundFaultSink();
        sink.Record("Unhandled exception in background work", new Exception("once"));

        Assert.AreEqual(1, sink.Drain().Count);
        Assert.AreEqual(0, sink.Drain().Count, "a fault must not be reported twice");
    }

    [TestMethod]
    public void Drain_SaysHowManyWereDiscarded()
    {
        var sink = new BackgroundFaultSink();

        // The cap drops the oldest first, and the oldest is often the one that explains the rest,
        // so a partial list has to admit that it is one.
        for (var i = 0; i < 130; i++)
            sink.Record("Unhandled exception in background work", new Exception($"failure {i}"));

        var outputs = sink.Drain();

        Assert.AreEqual(101, outputs.Count, "the cap holds 100, plus the line accounting for the rest");
        StringAssert.Contains(outputs[0].Content, "30");
        Assert.IsFalse(outputs.Any(o => o.Content.Contains("failure 0")), "the oldest go first");

        Assert.AreEqual(0, sink.Drain().Count, "the count resets with the queue");
    }

    [TestMethod]
    public void Record_WithNoException_StillReportsSomething()
    {
        var sink = new BackgroundFaultSink();
        sink.Record("Unhandled exception on a background thread", exception: null);

        var outputs = sink.Drain();

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].IsError);
    }

    // --- attribution ---

    [TestMethod]
    public void Record_GoesToTheOnlyNotebookThatIsRunning()
    {
        var running = new BackgroundFaultSink();
        var idle = new BackgroundFaultSink();

        using var runningRegistration = BackgroundFaultMonitor.Register(running);
        using var idleRegistration = BackgroundFaultMonitor.Register(idle);

        using (running.BeginExecution())
        {
            BackgroundFaultMonitor.Record("Unhandled exception in background work", new Exception("theirs"));
        }

        Assert.AreEqual(1, running.Drain().Count);
        Assert.AreEqual(0, idle.Drain().Count, "a notebook that was not running cannot have caused it");
    }

    [TestMethod]
    public void Record_WithNothingRunning_ReachesEveryNotebook()
    {
        var first = new BackgroundFaultSink();
        var second = new BackgroundFaultSink();

        using var firstRegistration = BackgroundFaultMonitor.Register(first);
        using var secondRegistration = BackgroundFaultMonitor.Register(second);

        BackgroundFaultMonitor.Record("Unhandled exception in background work", new Exception("unowned"));

        // Nothing says which notebook this belongs to, and a failure nobody is told about is worse
        // than one reported in more places than it happened.
        Assert.AreEqual(1, first.Drain().Count);
        Assert.AreEqual(1, second.Drain().Count);
    }

    [TestMethod]
    public void Record_WithSeveralRunning_ReachesEveryNotebook()
    {
        var first = new BackgroundFaultSink();
        var second = new BackgroundFaultSink();

        using var firstRegistration = BackgroundFaultMonitor.Register(first);
        using var secondRegistration = BackgroundFaultMonitor.Register(second);

        using (first.BeginExecution())
        using (second.BeginExecution())
        {
            BackgroundFaultMonitor.Record("Unhandled exception in background work", new Exception("ambiguous"));
        }

        Assert.AreEqual(1, first.Drain().Count);
        Assert.AreEqual(1, second.Drain().Count);
    }

    [TestMethod]
    public void Record_AfterANotebookCloses_DoesNotReachIt()
    {
        var closed = new BackgroundFaultSink();
        var open = new BackgroundFaultSink();

        var closedRegistration = BackgroundFaultMonitor.Register(closed);
        using var openRegistration = BackgroundFaultMonitor.Register(open);

        closedRegistration.Dispose();
        BackgroundFaultMonitor.Record("Unhandled exception in background work", new Exception("after"));

        Assert.AreEqual(0, closed.Drain().Count, "a closed notebook has no cell left to report against");
        Assert.AreEqual(1, open.Drain().Count);
    }

    [TestMethod]
    public void Record_WithNoNotebookOpen_IsHeldForTheHost()
    {
        BackgroundFaultMonitor.Record("Unhandled exception in background work", new Exception("hostless"));

        var outputs = BackgroundFaultMonitor.Drain();

        Assert.AreEqual(1, outputs.Count);
        StringAssert.Contains(outputs[0].Content, "hostless");
    }

    [TestMethod]
    public void BeginExecution_IsCounted()
    {
        var sink = new BackgroundFaultSink();
        Assert.IsFalse(sink.IsExecuting);

        var outer = sink.BeginExecution();
        var inner = sink.BeginExecution();

        inner.Dispose();
        Assert.IsTrue(sink.IsExecuting, "one cell finishing does not mean the notebook is idle");

        outer.Dispose();
        Assert.IsFalse(sink.IsExecuting);
    }

    [TestMethod]
    public void Install_IsIdempotent()
    {
        // Every host calls this without coordinating, so calling it repeatedly must not stack
        // handlers and report the same fault several times.
        BackgroundFaultMonitor.Install();
        BackgroundFaultMonitor.Install();
        BackgroundFaultMonitor.Install();

        var sink = new BackgroundFaultSink();
        using var registration = BackgroundFaultMonitor.Register(sink);

        BackgroundFaultMonitor.Record("Unhandled exception in background work", new Exception("single"));

        Assert.AreEqual(1, sink.Drain().Count);
    }
}
