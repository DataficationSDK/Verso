using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Verso.Abstractions;
using Verso.Python.Host;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.Tests.Interpreter;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Covers which handler receives a message from the subprocess. Two of them share the traffic: one
/// belonging to the running cell, which exists only while a cell runs, and one belonging to the
/// session, which is attached from the moment the subprocess starts. A message that reached
/// neither used to disappear without trace, and these tests are what say it no longer can.
/// </summary>
[TestClass]
public sealed class SessionEventDeliveryTests
{
    private static string Python => PythonProbe.Require();

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Distinctive enough that finding it anywhere cannot be a coincidence.</summary>
    private const string Marker = "written-with-no-cell-running";

    private static async Task<PythonHostSession> StartedSessionAsync()
    {
        var python = Python;
        var validator = new FakeInterpreterValidator().Valid(python, version: "3.13.0");
        var locator = new InterpreterLocator(validator, _ => null, () => Array.Empty<string>());

        var session = new PythonHostSession(new PythonKernelOptions { PythonExecutable = python }, locator: locator);
        await session.StartAsync(CancellationToken.None);
        return session;
    }

    private static string TextOf(IEnumerable<CellOutput> outputs)
        => string.Concat(outputs.Select(o => o.Content));

    /// <summary>
    /// Python that writes once, on a thread of its own, and not until a file appears. The file is
    /// what puts the write between two cells rather than inside one: a timer would have to be
    /// guessed at, and a guess that lands early makes this a test of the other handler.
    /// </summary>
    private static string WaitThenWrite(string gate) => $@"
import os, threading, time

def _write_when_released():
    while not os.path.exists({ToPythonString(gate)}):
        time.sleep(0.01)
    print({ToPythonString(Marker)})

threading.Thread(target=_write_when_released, daemon=True).start()
";

    private static string ToPythonString(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private static string ReserveGatePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"verso-session-event-{Guid.NewGuid():N}");
        Assert.IsFalse(File.Exists(path), "The gate has to start closed.");
        return path;
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task AMessageArrivingBetweenCellsReachesTheSessionHandler()
    {
        await using var session = await StartedSessionAsync();

        var unclaimed = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.EventUnclaimed += message => unclaimed.TrySetResult(message);

        var gate = ReserveGatePath();
        try
        {
            var started = await session.ExecuteAsync(WaitThenWrite(gate), new StubExecutionContext());
            Assert.IsFalse(started.Any(o => o.IsError), $"The cell that starts the thread failed: {TextOf(started)}");

            // The cell has returned, so nothing is executing and the write that follows belongs to
            // no cell at all.
            File.WriteAllText(gate, "");

            var message = await unclaimed.Task.WaitAsync(Timeout);

            Assert.AreEqual("stream", message["type"]!.GetValue<string>());
            StringAssert.Contains(message["text"]!.GetValue<string>(), Marker);
        }
        finally
        {
            try { File.Delete(gate); } catch { /* the gate is a temporary file either way */ }
        }
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task AMessageArrivingBetweenCellsIsNotHeldOverForTheNextOne()
    {
        await using var session = await StartedSessionAsync();

        var unclaimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.EventUnclaimed += _ => unclaimed.TrySetResult();

        var gate = ReserveGatePath();
        try
        {
            await session.ExecuteAsync(WaitThenWrite(gate), new StubExecutionContext());
            File.WriteAllText(gate, "");
            await unclaimed.Task.WaitAsync(Timeout);

            // The session handler took it, so the next cell must not also be given it. A message
            // delivered to both would show up here as output the cell never produced.
            var next = await session.ExecuteAsync("2 + 2", new StubExecutionContext());

            Assert.AreEqual("4", TextOf(next));
        }
        finally
        {
            try { File.Delete(gate); } catch { /* the gate is a temporary file either way */ }
        }
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task AMessageArrivingDuringACellReachesTheExecutionHandlerAlone()
    {
        await using var session = await StartedSessionAsync();

        // Filled from the read pump while this test's own thread reads it, so it is not a List.
        var unclaimed = new ConcurrentQueue<string?>();
        session.EventUnclaimed += message => unclaimed.Enqueue(message["type"]?.GetValue<string>());

        var outputs = await session.ExecuteAsync($"print({ToPythonString(Marker)})", new StubExecutionContext());

        StringAssert.Contains(TextOf(outputs), Marker, "The running cell's own handler did not receive its output.");
        Assert.AreEqual(
            0,
            unclaimed.Count,
            $"The session handler also took a message the cell owned: {string.Join(", ", unclaimed)}");
    }

    [TestMethod]
    public void TheTwoHandlersDivideTheMessageTypesBetweenThem()
    {
        // The types the running cell's handler answers, which is the switch in HandleEventAsync.
        // Everything else belongs to the session handler, and a type in neither list would be
        // read, parsed, and dropped, which is the failure this sub-phase exists to close.
        foreach (var messageType in new[] { "stream", "display", "input_request" })
        {
            Assert.IsTrue(
                HostProtocol.IsExecutionScoped(messageType),
                $"'{messageType}' is answered by the running cell's handler but is not named as belonging to it.");
        }

        foreach (var messageType in new[] { "comm", "execute_result", "hello", null, "" })
        {
            Assert.IsFalse(
                HostProtocol.IsExecutionScoped(messageType),
                $"'{messageType}' would be handed to a handler that does not answer it.");
        }
    }
}
