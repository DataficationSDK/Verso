using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Debugging;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;
using Verso.Testing.Fakes;

namespace Verso.Host.Tests.Handlers;

/// <summary>
/// The debug/* requests and the debug/stateChanged notification are the whole of what a client
/// knows about a stopped cell. The client never pieces the state together itself, so every change
/// has to reach it once and in full, a bad request has to be refused rather than guessed at, and
/// the requests that drive a stopped cell have to be the ones the host answers while a cell runs.
/// </summary>
[TestClass]
public class DebugHandlerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static async Task<(HostSession Session, string NotebookId, List<string> Notifications)>
        CreateOpenSessionAsync()
    {
        var notifications = new List<string>();
        var session = new HostSession(n => { lock (notifications) notifications.Add(n); });
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId, notifications);
    }

    private static JsonElement ToParams<T>(T value) =>
        JsonSerializer.SerializeToElement(value, JsonRpcMessage.SerializerOptions);

    private static async Task<JsonElement> DispatchAsync(
        HostSession session, string method, object @params)
    {
        var response = await session.DispatchAsync(1, method, ToParams(@params));
        return JsonDocument.Parse(response).RootElement;
    }

    private static int ErrorCodeOf(JsonElement response)
    {
        Assert.IsTrue(response.TryGetProperty("error", out var error),
            $"expected an error response, got: {response}");
        return error.GetProperty("code").GetInt32();
    }

    /// <summary>The params of every debug/stateChanged notification sent so far, oldest first.</summary>
    private static List<JsonElement> DebugStates(List<string> notifications)
    {
        string[] snapshot;
        lock (notifications) snapshot = notifications.ToArray();

        return snapshot
            .Select(text => JsonDocument.Parse(text).RootElement)
            .Where(root => root.TryGetProperty("method", out var method)
                && method.GetString() == MethodNames.DebugStateChanged)
            .Select(root => root.GetProperty("params"))
            .ToList();
    }

    // --- debug/setEnabled ---

    [TestMethod]
    public async Task SetEnabled_TurnsTheDebuggerOnAndOff_AndAnswersWithTheState()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);

            var on = DebugHandler.HandleSetEnabled(ns, ToParams(new { enabled = true }));
            Assert.IsTrue(ns.Scaffold.Debugger.Enabled);
            Assert.IsTrue(on.Enabled);
            Assert.IsNull(on.Stop);

            var off = DebugHandler.HandleSetEnabled(ns, ToParams(new { enabled = false }));
            Assert.IsFalse(ns.Scaffold.Debugger.Enabled);
            Assert.IsFalse(off.Enabled);
        }
    }

    [TestMethod]
    public async Task SetEnabled_ThroughTheDispatcher_ReturnsTheStateAsTheResult()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var response = await DispatchAsync(
                session, MethodNames.DebugSetEnabled, new { notebookId, enabled = true });

            var result = response.GetProperty("result");
            Assert.IsTrue(result.GetProperty("enabled").GetBoolean());
            Assert.AreEqual(0, result.GetProperty("breakpoints").GetArrayLength());
            Assert.IsFalse(result.TryGetProperty("stop", out _),
                "nothing is stopped, and the serializer drops a null rather than writing it");
        }
    }

    // --- debug/setBreakpoints ---

    [TestMethod]
    public async Task SetBreakpoints_ReplacesTheCellsLines()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);
            var cell = Guid.NewGuid();

            DebugHandler.HandleSetBreakpoints(ns, ToParams(new { cellId = cell.ToString(), lines = new[] { 5, 2 } }));
            var state = DebugHandler.HandleSetBreakpoints(
                ns, ToParams(new { cellId = cell.ToString(), lines = new[] { 9, 3 } }));

            CollectionAssert.AreEqual(new[] { 3, 9 }, ns.Scaffold.Debugger.GetBreakpoints(cell).ToArray(),
                "a second request replaces the lines rather than adding to them");
            Assert.AreEqual(1, state.Breakpoints.Count);
            Assert.AreEqual(cell.ToString(), state.Breakpoints[0].CellId);
            CollectionAssert.AreEqual(new[] { 3, 9 }, state.Breakpoints[0].Lines);
        }
    }

    [TestMethod]
    public async Task SetBreakpoints_LeavesOtherCellsAlone()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();

            DebugHandler.HandleSetBreakpoints(ns, ToParams(new { cellId = first.ToString(), lines = new[] { 1 } }));
            DebugHandler.HandleSetBreakpoints(ns, ToParams(new { cellId = second.ToString(), lines = new[] { 4 } }));

            CollectionAssert.AreEqual(new[] { 1 }, ns.Scaffold.Debugger.GetBreakpoints(first).ToArray());
            CollectionAssert.AreEqual(new[] { 4 }, ns.Scaffold.Debugger.GetBreakpoints(second).ToArray());
        }
    }

    [TestMethod]
    public async Task SetBreakpoints_AnEmptyListClearsTheCell()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);
            var cell = Guid.NewGuid();
            DebugHandler.HandleSetBreakpoints(ns, ToParams(new { cellId = cell.ToString(), lines = new[] { 2 } }));

            var state = DebugHandler.HandleSetBreakpoints(
                ns, ToParams(new { cellId = cell.ToString(), lines = Array.Empty<int>() }));

            Assert.AreEqual(0, ns.Scaffold.Debugger.GetBreakpoints(cell).Count);
            Assert.AreEqual(0, state.Breakpoints.Count,
                "a cleared cell is dropped from the state, not reported with no lines");
        }
    }

    [TestMethod]
    public async Task SetBreakpoints_NoLinesAtAllClearsTheCell()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);
            var cell = Guid.NewGuid();
            DebugHandler.HandleSetBreakpoints(ns, ToParams(new { cellId = cell.ToString(), lines = new[] { 2 } }));

            // A client that leaves the field out, or sends null, means the same as an empty list.
            DebugHandler.HandleSetBreakpoints(ns, ToParams(new { cellId = cell.ToString(), lines = (int[]?)null }));

            Assert.AreEqual(0, ns.Scaffold.Debugger.GetBreakpoints(cell).Count);
        }
    }

    [TestMethod]
    public async Task SetBreakpoints_IgnoresLinesBelowOne()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);
            var cell = Guid.NewGuid();

            var state = DebugHandler.HandleSetBreakpoints(
                ns, ToParams(new { cellId = cell.ToString(), lines = new[] { 0, 4, -3, 1 } }));

            CollectionAssert.AreEqual(new[] { 1, 4 }, state.Breakpoints.Single().Lines);
        }
    }

    // "(missing)" stands for leaving the field out altogether.
    [DataTestMethod]
    [DataRow("(missing)")]
    [DataRow("")]
    [DataRow("not-a-guid")]
    public async Task SetBreakpoints_WithoutAUsableCellId_IsRefused(string cellId)
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);
            var @params = cellId == "(missing)"
                ? ToParams(new { lines = new[] { 1 } })
                : ToParams(new { cellId, lines = new[] { 1 } });

            Assert.ThrowsException<JsonException>(() => DebugHandler.HandleSetBreakpoints(ns, @params));
            Assert.AreEqual(0, ns.Scaffold.Debugger.GetAllBreakpoints().Count);
        }
    }

    [TestMethod]
    public async Task SetBreakpoints_WithoutAUsableCellId_IsAnInvalidParamsError()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var missing = await DispatchAsync(
                session, MethodNames.DebugSetBreakpoints, new { notebookId, lines = new[] { 1 } });
            var malformed = await DispatchAsync(
                session, MethodNames.DebugSetBreakpoints, new { notebookId, cellId = "cell-1", lines = new[] { 1 } });
            var numeric = await DispatchAsync(
                session, MethodNames.DebugSetBreakpoints, new { notebookId, cellId = 7, lines = new[] { 1 } });

            Assert.AreEqual(JsonRpcMessage.ErrorCodes.InvalidParams, ErrorCodeOf(missing));
            Assert.AreEqual(JsonRpcMessage.ErrorCodes.InvalidParams, ErrorCodeOf(malformed));
            Assert.AreEqual(JsonRpcMessage.ErrorCodes.InvalidParams, ErrorCodeOf(numeric));
        }
    }

    [TestMethod]
    public async Task SetBreakpoints_WithoutParams_IsRefused()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);
            Assert.ThrowsException<JsonException>(() => DebugHandler.HandleSetBreakpoints(ns, null));
            Assert.ThrowsException<JsonException>(() => DebugHandler.HandleSetEnabled(ns, null));
            Assert.ThrowsException<JsonException>(() => DebugHandler.HandleResume(ns, null));
        }
    }

    // --- debug/resume ---

    [DataTestMethod]
    [DataRow("continue")]
    [DataRow("stepOver")]
    [DataRow("stepInto")]
    [DataRow("stepOut")]
    [DataRow("STEPOVER")]
    [DataRow("Continue")]
    public async Task Resume_WithNothingStopped_AnswersNotResumed(string action)
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);

            var result = DebugHandler.HandleResume(ns, ToParams(new { action }));

            Assert.IsFalse(result.Resumed);
        }
    }

    [TestMethod]
    public async Task Resume_ThroughTheDispatcher_ReportsResumedFalse()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var response = await DispatchAsync(
                session, MethodNames.DebugResume, new { notebookId, action = "continue" });

            Assert.IsFalse(response.GetProperty("result").GetProperty("resumed").GetBoolean());
        }
    }

    [DataTestMethod]
    [DataRow("jump")]
    [DataRow("step")]
    [DataRow("1")]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("+1")]
    [DataRow(" 1")]
    [DataRow("continue,stepOver")]
    [DataRow("")]
    [DataRow("   ")]
    public async Task Resume_RefusesAnActionItDoesNotKnow(string action)
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);

            Assert.ThrowsException<JsonException>(
                () => DebugHandler.HandleResume(ns, ToParams(new { action })));
        }
    }

    [TestMethod]
    public async Task Resume_WithABadAction_IsAnInvalidParamsError()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var unknown = await DispatchAsync(session, MethodNames.DebugResume, new { notebookId, action = "jump" });
            var missing = await DispatchAsync(session, MethodNames.DebugResume, new { notebookId });
            var number = await DispatchAsync(session, MethodNames.DebugResume, new { notebookId, action = 1 });

            Assert.AreEqual(JsonRpcMessage.ErrorCodes.InvalidParams, ErrorCodeOf(unknown));
            Assert.AreEqual(JsonRpcMessage.ErrorCodes.InvalidParams, ErrorCodeOf(missing));
            Assert.AreEqual(JsonRpcMessage.ErrorCodes.InvalidParams, ErrorCodeOf(number));
        }
    }

    // --- Snapshot ---

    [TestMethod]
    public void Snapshot_ListsEachCellOnce_InCellOrder_WithItsLinesAscending()
    {
        var debugger = new NotebookDebugger();
        var cells = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // Set in an order unrelated to how they sort, with each cell's lines out of order too.
        debugger.SetBreakpoints(cells[2], new[] { 8, 2 });
        debugger.SetBreakpoints(cells[0], new[] { 5 });
        debugger.SetBreakpoints(cells[1], new[] { 3, 1, 3 });

        var state = DebugHandler.Snapshot(debugger);

        var expectedOrder = cells.OrderBy(id => id).Select(id => id.ToString()).ToArray();
        CollectionAssert.AreEqual(expectedOrder, state.Breakpoints.Select(b => b.CellId).ToArray(),
            "the same breakpoints always arrive in the same order, so a client can compare states");
        CollectionAssert.AreEqual(new[] { 2, 8 }, state.Breakpoints.Single(b => b.CellId == cells[2].ToString()).Lines);
        CollectionAssert.AreEqual(new[] { 1, 3 }, state.Breakpoints.Single(b => b.CellId == cells[1].ToString()).Lines);
        Assert.IsFalse(state.Enabled);
        Assert.IsNull(state.Stop);
    }

    [TestMethod]
    public async Task Snapshot_MapsAStop_InnermostFrameFirst_WithItsVariables()
    {
        await using var scaffold = new Scaffold();
        var earlierCell = Guid.NewGuid();
        var kernel = new StoppingKernel(cellId => new DebugStop(DebugStopReason.Step, new[]
        {
            new DebugFrame("Model", earlierCell, 4, new[]
            {
                new DebugVariable("x", "int", "7"),
                new DebugVariable("basis", "double", "23.15"),
            }),
            new DebugFrame("", cellId, 2, new[] { new DebugVariable("label", "string", "\"north\"") }),
            new DebugFrame("Render", null, 0, Array.Empty<DebugVariable>()),
        }));
        scaffold.DefaultKernelId = kernel.LanguageId;
        scaffold.RegisterKernel(kernel);
        var cell = scaffold.AddCell(source: "first\nsecond");
        scaffold.Debugger.Enabled = true;

        var running = Task.Run(() => scaffold.ExecuteCellAsync(cell.Id));
        await WaitUntilAsync(() => scaffold.Debugger.CurrentStop is not null, "the kernel to report its stop");

        var stop = DebugHandler.Snapshot(scaffold.Debugger).Stop;

        Assert.IsNotNull(stop);
        Assert.AreEqual("step", stop!.Reason);
        Assert.AreEqual(3, stop.Frames.Count);

        Assert.AreEqual("Model", stop.Frames[0].Name);
        Assert.AreEqual(earlierCell.ToString(), stop.Frames[0].CellId);
        Assert.AreEqual(4, stop.Frames[0].Line);
        CollectionAssert.AreEqual(new[] { "x", "basis" }, stop.Frames[0].Variables.Select(v => v.Name).ToArray());
        Assert.AreEqual("int", stop.Frames[0].Variables[0].TypeName);
        Assert.AreEqual("7", stop.Frames[0].Variables[0].Value);
        Assert.AreEqual("double", stop.Frames[0].Variables[1].TypeName);
        Assert.AreEqual("23.15", stop.Frames[0].Variables[1].Value);

        Assert.AreEqual("", stop.Frames[1].Name);
        Assert.AreEqual(cell.Id.ToString(), stop.Frames[1].CellId);
        Assert.AreEqual(2, stop.Frames[1].Line);
        Assert.AreEqual("\"north\"", stop.Frames[1].Variables.Single().Value);

        // Code that is in no cell, such as a library the cell called into, has no cell to name.
        Assert.AreEqual("Render", stop.Frames[2].Name);
        Assert.IsNull(stop.Frames[2].CellId);
        Assert.AreEqual(0, stop.Frames[2].Line);
        Assert.AreEqual(0, stop.Frames[2].Variables.Count);

        Assert.IsTrue(scaffold.Debugger.Resume(DebugResumeAction.Continue));
        await running.WaitAsync(Patience);
        Assert.IsNull(DebugHandler.Snapshot(scaffold.Debugger).Stop);
    }

    [TestMethod]
    public async Task Snapshot_NamesABreakpointStopAsSuch()
    {
        await using var scaffold = new Scaffold();
        var kernel = new StoppingKernel(cellId => new DebugStop(DebugStopReason.Breakpoint, new[]
        {
            new DebugFrame("", cellId, 1, Array.Empty<DebugVariable>()),
        }));
        scaffold.DefaultKernelId = kernel.LanguageId;
        scaffold.RegisterKernel(kernel);
        var cell = scaffold.AddCell(source: "only");
        scaffold.Debugger.Enabled = true;

        var running = Task.Run(() => scaffold.ExecuteCellAsync(cell.Id));
        await WaitUntilAsync(() => scaffold.Debugger.CurrentStop is not null, "the kernel to report its stop");

        Assert.AreEqual("breakpoint", DebugHandler.Snapshot(scaffold.Debugger).Stop!.Reason);

        scaffold.Debugger.Resume(DebugResumeAction.Continue);
        await running.WaitAsync(Patience);
    }

    // --- debug/stateChanged ---

    [TestMethod]
    public async Task EachChange_SendsOneStateChangedNotification_CarryingTheWholeState()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        await using (session)
        {
            var cell = Guid.NewGuid();
            Assert.AreEqual(0, DebugStates(notifications).Count, "opening a notebook changes nothing");

            await DispatchAsync(session, MethodNames.DebugSetEnabled, new { notebookId, enabled = true });
            var states = DebugStates(notifications);
            Assert.AreEqual(1, states.Count);
            Assert.AreEqual(notebookId, states[0].GetProperty("notebookId").GetString());
            Assert.IsTrue(states[0].GetProperty("enabled").GetBoolean());
            Assert.AreEqual(0, states[0].GetProperty("breakpoints").GetArrayLength());
            Assert.IsFalse(states[0].TryGetProperty("stop", out _));

            await DispatchAsync(session, MethodNames.DebugSetBreakpoints,
                new { notebookId, cellId = cell.ToString(), lines = new[] { 6, 2 } });
            states = DebugStates(notifications);
            Assert.AreEqual(2, states.Count);
            Assert.AreEqual(notebookId, states[1].GetProperty("notebookId").GetString());
            Assert.IsTrue(states[1].GetProperty("enabled").GetBoolean());
            var breakpoints = states[1].GetProperty("breakpoints");
            Assert.AreEqual(1, breakpoints.GetArrayLength());
            Assert.AreEqual(cell.ToString(), breakpoints[0].GetProperty("cellId").GetString());
            CollectionAssert.AreEqual(new[] { 2, 6 },
                breakpoints[0].GetProperty("lines").EnumerateArray().Select(l => l.GetInt32()).ToArray());
            Assert.IsFalse(states[1].TryGetProperty("stop", out _));

            await DispatchAsync(session, MethodNames.DebugSetEnabled, new { notebookId, enabled = false });
            states = DebugStates(notifications);
            Assert.AreEqual(3, states.Count);
            Assert.IsFalse(states[2].GetProperty("enabled").GetBoolean());
            Assert.AreEqual(1, states[2].GetProperty("breakpoints").GetArrayLength(),
                "turning debugging off keeps the breakpoints for next time");
        }
    }

    [TestMethod]
    public async Task ARequestThatChangesNothing_SendsNothing()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        await using (session)
        {
            var cell = Guid.NewGuid().ToString();
            await DispatchAsync(session, MethodNames.DebugSetEnabled, new { notebookId, enabled = true });
            await DispatchAsync(session, MethodNames.DebugSetBreakpoints, new { notebookId, cellId = cell, lines = new[] { 3 } });
            var before = DebugStates(notifications).Count;

            await DispatchAsync(session, MethodNames.DebugSetEnabled, new { notebookId, enabled = true });
            await DispatchAsync(session, MethodNames.DebugSetBreakpoints, new { notebookId, cellId = cell, lines = new[] { 3 } });
            await DispatchAsync(session, MethodNames.DebugResume, new { notebookId, action = "continue" });

            Assert.AreEqual(before, DebugStates(notifications).Count);
        }
    }

    [TestMethod]
    public async Task ARefusedRequest_SendsNothing()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        await using (session)
        {
            await DispatchAsync(session, MethodNames.DebugSetBreakpoints, new { notebookId, cellId = "nope", lines = new[] { 3 } });
            await DispatchAsync(session, MethodNames.DebugResume, new { notebookId, action = "jump" });

            Assert.AreEqual(0, DebugStates(notifications).Count);
        }
    }

    [TestMethod]
    public async Task AfterTheSessionIsDisposed_NothingMoreIsSent()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);
        await DispatchAsync(session, MethodNames.DebugSetEnabled, new { notebookId, enabled = true });
        var before = DebugStates(notifications).Count;

        // Disposing the notebook turns debugging off on its way out, which would otherwise be
        // announced to a client that has already let the session go.
        await session.RemoveSessionAsync(notebookId);
        Assert.IsFalse(ns.Scaffold.Debugger.Enabled);

        // And a debugger still reachable through a stale reference is no longer listened to.
        ns.Scaffold.Debugger.SetBreakpoints(Guid.NewGuid(), new[] { 1 });

        Assert.AreEqual(before, DebugStates(notifications).Count);
    }

    [TestMethod]
    public async Task ClosingTheNotebook_SendsNothingMore()
    {
        var (session, notebookId, notifications) = await CreateOpenSessionAsync();
        await DispatchAsync(session, MethodNames.DebugSetEnabled, new { notebookId, enabled = true });
        var before = DebugStates(notifications).Count;

        var closed = await DispatchAsync(session, MethodNames.NotebookClose, new { notebookId });

        Assert.IsTrue(closed.TryGetProperty("result", out _), $"notebook/close failed: {closed}");
        Assert.AreEqual(before, DebugStates(notifications).Count);
    }

    // --- what is answered while a cell runs ---

    [DataTestMethod]
    [DataRow(MethodNames.DebugSetEnabled)]
    [DataRow(MethodNames.DebugSetBreakpoints)]
    [DataRow(MethodNames.DebugResume)]
    [DataRow(MethodNames.ExtensionConsentResponse)]
    [DataRow(MethodNames.InputResponse)]
    [DataRow(MethodNames.ExecutionCancel)]
    public void IsAnsweredWhileRunning_ForWhatAStoppedOrWaitingCellNeeds(string method)
    {
        Assert.IsTrue(HostSession.IsAnsweredWhileRunning(method));
    }

    [DataTestMethod]
    [DataRow(MethodNames.ExecutionRun)]
    [DataRow(MethodNames.ExecutionRunAll)]
    [DataRow(MethodNames.KernelRestart)]
    [DataRow(MethodNames.CellUpdateSource)]
    [DataRow(MethodNames.NotebookOpen)]
    [DataRow("debug/unknown")]
    [DataRow("")]
    public void IsAnsweredWhileRunning_NotForAnythingElse(string method)
    {
        Assert.IsFalse(HostSession.IsAnsweredWhileRunning(method));
    }

    [TestMethod]
    public void IsAnsweredWhileRunning_MatchesTheMethodNameExactly()
    {
        // The reader compares the name as the client sent it, so a near miss takes the queue.
        Assert.IsFalse(HostSession.IsAnsweredWhileRunning("Debug/Resume"));
        Assert.IsFalse(HostSession.IsAnsweredWhileRunning("debug/resume "));
    }

    // --- notebook/getLanguages ---

    [TestMethod]
    public async Task GetLanguages_ReportsWhichKernelsCanPause()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var ns = session.GetSession(notebookId);
            ns.Scaffold.RegisterKernel(new FakeLanguageKernel("plain"));

            var languages = NotebookHandler.HandleGetLanguages(ns).Languages;

            var csharp = languages.SingleOrDefault(l => l.Id == "csharp");
            Assert.IsNotNull(csharp, "the built-in C# kernel should be registered");
            Assert.IsTrue(csharp!.SupportsDebugging);

            var plain = languages.Single(l => l.Id == "plain");
            Assert.IsFalse(plain.SupportsDebugging);

            // The markup kernels that ship with the host have nothing to step through.
            foreach (var id in new[] { "html", "mermaid" })
            {
                var markup = languages.SingleOrDefault(l => l.Id == id);
                if (markup is not null)
                    Assert.IsFalse(markup.SupportsDebugging, $"{id} cannot pause");
            }
        }
    }

    [TestMethod]
    public async Task GetLanguages_ThroughTheDispatcher_CarriesSupportsDebugging()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        await using (session)
        {
            var response = await DispatchAsync(session, MethodNames.NotebookGetLanguages, new { notebookId });

            var csharp = response.GetProperty("result").GetProperty("languages").EnumerateArray()
                .Single(l => l.GetProperty("id").GetString() == "csharp");
            Assert.IsTrue(csharp.GetProperty("supportsDebugging").GetBoolean());
        }
    }

    // --- helpers ---

    private static async Task WaitUntilAsync(Func<bool> condition, string waitingFor)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting for {waitingFor}.");
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// A kernel that stops once with whatever report it is built with, so the mapping of a stop
    /// can be checked against frames no real kernel would be asked to produce on cue.
    /// </summary>
    private sealed class StoppingKernel : ILanguageKernel, IDebuggableKernel
    {
        private readonly Func<Guid, DebugStop> _stop;

        public StoppingKernel(Func<Guid, DebugStop> stop) => _stop = stop;

        public string ExtensionId => "com.test.debughandler";
        public string Name => "Stopping";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public string LanguageId => "stopping";
        public string DisplayName => "Stopping";
        public IReadOnlyList<string> FileExtensions => Array.Empty<string>();

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
        public Task InitializeAsync() => Task.CompletedTask;

        public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
            => Task.FromResult<IReadOnlyList<CellOutput>>(Array.Empty<CellOutput>());

        public async Task<IReadOnlyList<CellOutput>> DebugAsync(
            string code, IExecutionContext context, IDebugSession session)
        {
            await session.StopAsync(_stop(context.CellId));
            return Array.Empty<CellOutput>();
        }

        public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
            => Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());

        public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
            => Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());

        public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
            => Task.FromResult<HoverInfo?>(null);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
