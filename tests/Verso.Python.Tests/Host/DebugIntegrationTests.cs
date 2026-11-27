using System.Text.Json.Nodes;
using Verso.Abstractions;
using Verso.Python.Host;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.Tests.Interpreter;
using Verso.Testing.Fakes;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Stops a cell at a breakpoint in a real Python subprocess and drives it from the debugging
/// conversation a kernel is handed. Where a cell stops and how stepping moves are covered against
/// the host script directly; what is measured here is that a stop crosses the process boundary,
/// waits, and is answered.
/// </summary>
[TestClass]
public sealed class DebugIntegrationTests
{
    private const string Square = "def square(n):\n    result = n * n\n    return result";

    private static async Task<PythonHostSession> StartedSessionAsync()
    {
        var python = PythonProbe.Require();
        var validator = new FakeInterpreterValidator().Valid(python, version: "3.13.0");
        var locator = new InterpreterLocator(validator, _ => null, () => Array.Empty<string>());

        var session = new PythonHostSession(
            new PythonKernelOptions { PythonExecutable = python }, locator: locator);
        await session.StartAsync(CancellationToken.None);
        return session;
    }

    [TestMethod]
    public void Kernel_CanPauseARunningCell()
    {
        Assert.IsInstanceOfType<IDebuggableKernel>(new PythonKernel());
    }

    [TestMethod]
    public async Task Breakpoint_StopsTheCell_AndReportsWhereAndWhat()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        var debug = new FakeDebugSession().SetBreakpoints(context.CellId, 3);

        var outputs = await session.ExecuteAsync("x = 1\ny = x + 1\nz = y * 2\nz", context, debug);

        Assert.AreEqual("4", outputs.Single().Content);

        var stop = debug.Stops.Single();
        Assert.AreEqual(DebugStopReason.Breakpoint, stop.Reason);

        var frame = stop.Frames.Single();
        Assert.AreEqual("", frame.Name, "Code outside any function is the cell itself.");
        Assert.AreEqual(context.CellId, frame.CellId);
        Assert.AreEqual(3, frame.Line);
        CollectionAssert.AreEqual(
            new[] { "x:int=1", "y:int=2" },
            frame.Variables.Select(v => $"{v.Name}:{v.TypeName}={v.Value}").ToArray());
    }

    [TestMethod]
    public async Task StepInto_EntersAFunctionAnEarlierCellDefined_AndStepOutReturns()
    {
        await using var session = await StartedSessionAsync();

        // Run without debugging, as it would have been before anything went wrong.
        var defining = new StubExecutionContext();
        await session.ExecuteAsync(Square, defining);

        var calling = new StubExecutionContext();
        var debug = new FakeDebugSession(DebugResumeAction.StepInto, DebugResumeAction.StepOut)
            .SetBreakpoints(calling.CellId, 1);

        var outputs = await session.ExecuteAsync("a = square(3)\nb = a + 1\nb", calling, debug);

        Assert.AreEqual("10", outputs.Single().Content);
        Assert.AreEqual(3, debug.Stops.Count);

        var inside = debug.Stops[1];
        Assert.AreEqual(DebugStopReason.Step, inside.Reason);
        CollectionAssert.AreEqual(
            new[] { "square", "" },
            inside.Frames.Select(f => f.Name).ToArray());
        Assert.AreEqual(defining.CellId, inside.Frames[0].CellId);
        Assert.AreEqual(2, inside.Frames[0].Line);
        Assert.AreEqual(calling.CellId, inside.Frames[1].CellId);
        Assert.AreEqual(1, inside.Frames[1].Line);

        var back = debug.Stops[2];
        Assert.AreEqual(calling.CellId, back.Frames[0].CellId);
        Assert.AreEqual(2, back.Frames[0].Line);
    }

    [TestMethod]
    public async Task BreakpointSetWhileStopped_IsHitInTheSameRun()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        var debug = new FakeDebugSession().SetBreakpoints(context.CellId, 1);

        // The change is sent to the host as its own message, behind which the answer to the
        // stop follows on the same socket, so the host has the new line before it carries on.
        debug.OnStop = stop =>
        {
            if (stop.Frames[0].Line == 1)
                debug.SetBreakpoints(context.CellId, 3);
        };

        await session.ExecuteAsync("a = 1\nb = 2\nc = 3\nc", context, debug);

        CollectionAssert.AreEqual(
            new[] { 1, 3 },
            debug.Stops.Select(s => s.Frames[0].Line).ToArray());
    }

    [TestMethod]
    public async Task Cancel_WhileStopped_EndsTheCellAndKeepsTheInterpreter()
    {
        await using var session = await StartedSessionAsync();

        using var cancellation = new CancellationTokenSource();
        var context = new StubExecutionContext { CancellationToken = cancellation.Token };
        var debug = new FakeDebugSession().SetBreakpoints(context.CellId, 2);
        debug.OnStop = _ => cancellation.Cancel();

        await session.ExecuteAsync("marker = 'before'\nmarker = 'after'", context, debug);

        Assert.AreEqual(1, debug.Stops.Count);

        // The line the cell was stopped at never ran, and the interpreter is still there.
        var outputs = await session.ExecuteAsync("marker", new StubExecutionContext());
        Assert.AreEqual("'before'", outputs[0].Content);
    }

    [TestMethod]
    public async Task InterpreterDying_WhileStopped_EndsTheWaitAtTheStop()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();
        var debug = new WaitingDebugSession(context.CellId, 3);

        var run = session.ExecuteAsync("import os\npid = os.getpid()\nx = 1", context, debug);

        var stop = await debug.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(60));
        var pid = int.Parse(stop.Frames[0].Variables.Single(v => v.Name == "pid").Value);

        // Nothing cancels the cell and nothing answers the stop: the process simply goes.
        using (var interpreter = System.Diagnostics.Process.GetProcessById(pid))
            interpreter.Kill();

        var outputs = await run.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.IsTrue(outputs.Any(o => o.IsError), "The cell should say its interpreter went away.");

        // A wait left behind would go on holding the notebook's one stop, and the next cell to
        // reach a breakpoint would never get to report it.
        await debug.WaitEnded.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>A conversation that is never answered: a stop waits until it is told to give up.</summary>
    private sealed class WaitingDebugSession : IDebugSession
    {
        private readonly Guid _cellId;
        private readonly int[] _lines;

        public WaitingDebugSession(Guid cellId, params int[] lines)
        {
            _cellId = cellId;
            _lines = lines;
        }

        public TaskCompletionSource<DebugStop> Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> WaitEnded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

#pragma warning disable CS0067 // never raised: the breakpoints here do not change
        public event Action? BreakpointsChanged;
#pragma warning restore CS0067

        public IReadOnlyCollection<int> GetBreakpoints(Guid cellId)
            => cellId == _cellId ? _lines : Array.Empty<int>();

        public async Task<DebugResumeAction> StopAsync(DebugStop stop, CancellationToken cancellationToken = default)
        {
            Stopped.TrySetResult(stop);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            finally
            {
                WaitEnded.TrySetResult(true);
            }

            return DebugResumeAction.Continue;
        }
    }

    [TestMethod]
    public async Task WithoutADebugSession_BreakpointsAreNotAsked()
    {
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync("x = 1\nx + 1", new StubExecutionContext());

        Assert.AreEqual("2", outputs.Single().Content);
    }

    // --- reading a stop ---

    [TestMethod]
    public void ReadStop_MapsFramesInnermostFirst()
    {
        var cell = Guid.NewGuid();
        var message = new JsonObject
        {
            ["type"] = "debug_stopped",
            ["req_id"] = 4,
            ["reason"] = "step",
            ["frames"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "inner",
                    ["cell"] = cell.ToString(),
                    ["line"] = 7,
                    ["variables"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "n", ["type"] = "int", ["value"] = "3" },
                    },
                },
                new JsonObject
                {
                    ["name"] = "",
                    ["cell"] = null,
                    ["line"] = 2,
                    ["variables"] = new JsonArray(),
                },
            },
        };

        var stop = PythonHostSession.ReadStop(message)!;

        Assert.AreEqual(DebugStopReason.Step, stop.Reason);
        Assert.AreEqual(2, stop.Frames.Count);
        Assert.AreEqual("inner", stop.Frames[0].Name);
        Assert.AreEqual(cell, stop.Frames[0].CellId);
        Assert.AreEqual(7, stop.Frames[0].Line);
        Assert.AreEqual(new DebugVariable("n", "int", "3"), stop.Frames[0].Variables.Single());
        Assert.IsNull(stop.Frames[1].CellId, "A frame the host could not place belongs to no cell.");
    }

    [TestMethod]
    public void ReadStop_WithNoFrames_IsNoStop()
    {
        var message = new JsonObject
        {
            ["type"] = "debug_stopped",
            ["reason"] = "breakpoint",
            ["frames"] = new JsonArray(),
        };

        Assert.IsNull(PythonHostSession.ReadStop(message));
    }
}
