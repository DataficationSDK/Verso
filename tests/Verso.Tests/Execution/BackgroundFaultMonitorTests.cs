using Verso.Abstractions;
using Verso.Execution;

namespace Verso.Tests.Execution;

/// <summary>
/// Cell code can start work and return before that work finishes. When it then throws, there is no
/// longer a result to carry the failure, and .NET discards an unobserved task exception silently.
/// These cover the queue that holds such failures until there is a cell to report them against.
/// </summary>
[TestClass]
public sealed class BackgroundFaultMonitorTests
{
    [TestCleanup]
    public void Cleanup()
    {
        // The queue is process-wide, so anything left behind would surface in another test.
        BackgroundFaultMonitor.Drain();
    }

    [TestMethod]
    public void Drain_WithNothingRecorded_ReturnsEmpty()
    {
        Assert.AreEqual(0, BackgroundFaultMonitor.Drain().Count);
    }

    [TestMethod]
    public void Drain_ReportsARecordedFaultAsAFailure()
    {
        BackgroundFaultMonitor.Record("Unhandled exception in background work", new InvalidOperationException("late failure"));

        var outputs = BackgroundFaultMonitor.Drain();

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
        BackgroundFaultMonitor.Record("Unhandled exception in background work", new Exception("once"));

        Assert.AreEqual(1, BackgroundFaultMonitor.Drain().Count);
        Assert.AreEqual(0, BackgroundFaultMonitor.Drain().Count, "a fault must not be reported twice");
    }

    [TestMethod]
    public void Record_WithNoException_StillReportsSomething()
    {
        BackgroundFaultMonitor.Record("Unhandled exception on a background thread", exception: null);

        var outputs = BackgroundFaultMonitor.Drain();

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].IsError);
    }

    [TestMethod]
    public void Install_IsIdempotent()
    {
        // Every host calls this without coordinating, so calling it repeatedly must not stack
        // handlers and report the same fault several times.
        BackgroundFaultMonitor.Install();
        BackgroundFaultMonitor.Install();
        BackgroundFaultMonitor.Install();

        BackgroundFaultMonitor.Record("Unhandled exception in background work", new Exception("single"));

        Assert.AreEqual(1, BackgroundFaultMonitor.Drain().Count);
    }
}
