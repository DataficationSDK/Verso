using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

namespace Verso.Host.Tests.Handlers;

/// <summary>
/// A C# cell stopped at a breakpoint, driven the way a client drives it. The run's own request is
/// still unanswered while the cell is stopped, so everything that moves the cell on has to be
/// answered alongside it. These start a run, leave it waiting, and steer it with further requests
/// to the same session, the way the host's reader thread does while the main loop sits in the run.
/// </summary>
[TestClass]
[DoNotParallelize] // Console.Out is process-wide, and the C# kernel captures it while a cell runs
public class DebugStopDispatchTests
{
    // The first C# cell in a process pays for the compiler warming up.
    private static readonly TimeSpan FirstStopPatience = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private const string Code =
        "var a = 1;\n" +
        "var b = a + 41;\n" +
        "var c = b * 2;\n" +
        "Console.WriteLine($\"c = {c}\");";

    /// <summary>Every line the host would have written to the client, safe to read while it writes.</summary>
    private sealed class Sent
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = new();

        public void Add(string text)
        {
            lock (_gate) _lines.Add(text);
        }

        public JsonElement[] Messages()
        {
            string[] snapshot;
            lock (_gate) snapshot = _lines.ToArray();
            return snapshot.Select(text => JsonDocument.Parse(text).RootElement).ToArray();
        }

        public JsonElement[] DebugStates() => Messages()
            .Where(m => m.TryGetProperty("method", out var method)
                && method.GetString() == MethodNames.DebugStateChanged)
            .Select(m => m.GetProperty("params"))
            .ToArray();

        public bool HasDebugState(Func<JsonElement, bool> match) => DebugStates().Any(match);

        public JsonElement? Response(long id) => Messages()
            .Where(m => m.TryGetProperty("id", out var mid)
                && mid.ValueKind == JsonValueKind.Number && mid.GetInt64() == id)
            .Select(m => (JsonElement?)m)
            .FirstOrDefault();
    }

    private static async Task<(HostSession Session, string NotebookId, Sent Sent)> OpenAsync()
    {
        var sent = new Sent();
        var session = new HostSession(sent.Add);
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" }, JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId, sent);
    }

    private static async Task<JsonElement> DispatchAsync(
        HostSession session, int id, string method, object @params)
    {
        var response = await session.DispatchAsync(
            id, method, JsonSerializer.SerializeToElement(@params, JsonRpcMessage.SerializerOptions));
        return JsonDocument.Parse(response).RootElement;
    }

    private static JsonElement ResultOf(JsonElement response, string what)
    {
        Assert.IsTrue(response.TryGetProperty("result", out var result), $"{what} failed: {response}");
        return result;
    }

    /// <summary>
    /// Waits for a notification that matches, failing early if the run has already ended, since
    /// then nothing more is coming and its response says why.
    /// </summary>
    private static async Task<JsonElement> WaitForStopAsync(
        Sent sent, Task<JsonElement> run, Func<JsonElement, bool> match, TimeSpan patience, string waitingFor)
    {
        var deadline = DateTime.UtcNow + patience;
        while (DateTime.UtcNow < deadline)
        {
            var found = sent.DebugStates().FirstOrDefault(match);
            if (found.ValueKind == JsonValueKind.Object)
                return found;

            if (run.IsCompleted)
                Assert.Fail($"The run ended while waiting for {waitingFor}: {await run}");

            await Task.Delay(20);
        }

        Assert.Fail($"Timed out after {patience.TotalSeconds:0}s waiting for {waitingFor}. "
            + $"States seen: {string.Join(" | ", sent.DebugStates().Select(s => s.ToString()))}");
        return default;
    }

    private static async Task<JsonElement> WaitForRunAsync(Task<JsonElement> run, string what)
    {
        if (await Task.WhenAny(run, Task.Delay(Patience)) != run)
            Assert.Fail($"Timed out after {Patience.TotalSeconds:0}s waiting for {what}.");
        return await run;
    }

    private static bool StopsAt(JsonElement state, string cellId, int line, string reason)
    {
        if (!state.TryGetProperty("stop", out var stop) || stop.ValueKind != JsonValueKind.Object)
            return false;

        var top = stop.GetProperty("frames")[0];
        return stop.GetProperty("reason").GetString() == reason
            && top.GetProperty("cellId").GetString() == cellId
            && top.GetProperty("line").GetInt32() == line;
    }

    private static bool HasStop(JsonElement state)
        => state.TryGetProperty("stop", out var stop) && stop.ValueKind == JsonValueKind.Object;

    private static Dictionary<string, string> VariablesOf(JsonElement state)
        => state.GetProperty("stop").GetProperty("frames")[0].GetProperty("variables").EnumerateArray()
            .ToDictionary(v => v.GetProperty("name").GetString()!, v => v.GetProperty("value").GetString()!);

    private static async Task<(string CellId, Task<JsonElement> Run)> StartStoppedRunAsync(
        HostSession session, string notebookId, string code, int breakpointLine)
    {
        var cell = session.GetSession(notebookId).Scaffold.AddCell("code", "csharp", code);
        var cellId = cell.Id.ToString();

        ResultOf(await DispatchAsync(session, 1, MethodNames.DebugSetEnabled,
            new { notebookId, enabled = true }), "debug/setEnabled");
        ResultOf(await DispatchAsync(session, 2, MethodNames.DebugSetBreakpoints,
            new { notebookId, cellId, lines = new[] { breakpointLine } }), "debug/setBreakpoints");

        // On a thread of its own, the way the host's main loop runs it, and deliberately not
        // awaited: the response to this request is not written until the cell is let go.
        var run = Task.Run(() => DispatchAsync(session, 3, MethodNames.ExecutionRun, new { notebookId, cellId }));
        return (cellId, run);
    }

    [TestMethod]
    public async Task AStoppedCell_StepsAndContinues_ThroughRequestsAnsweredWhileItRuns()
    {
        var (session, notebookId, sent) = await OpenAsync();
        await using (session)
        {
            var (cellId, run) = await StartStoppedRunAsync(session, notebookId, Code, breakpointLine: 2);

            var atBreakpoint = await WaitForStopAsync(sent, run,
                s => StopsAt(s, cellId, 2, "breakpoint"), FirstStopPatience,
                $"a stop at the breakpoint on line 2 of cell {cellId}");

            Assert.AreEqual(notebookId, atBreakpoint.GetProperty("notebookId").GetString());
            Assert.IsTrue(atBreakpoint.GetProperty("enabled").GetBoolean());
            Assert.AreEqual(cellId, atBreakpoint.GetProperty("breakpoints")[0].GetProperty("cellId").GetString());
            Assert.AreEqual("", atBreakpoint.GetProperty("stop").GetProperty("frames")[0].GetProperty("name").GetString());

            var before = VariablesOf(atBreakpoint);
            Assert.AreEqual("1", before["a"]);
            Assert.IsFalse(before.ContainsKey("c"), "c is not assigned until line 3");
            Assert.IsFalse(run.IsCompleted, "the run holds while its cell is stopped");

            // Step to the next line. This is answered while the run's own request is outstanding,
            // which is the case that would hang if it had to wait its turn behind the run.
            var stepped = await DispatchAsync(session, 4, MethodNames.DebugResume, new { notebookId, action = "stepOver" });
            Assert.IsTrue(ResultOf(stepped, "debug/resume stepOver").GetProperty("resumed").GetBoolean());

            var afterStep = await WaitForStopAsync(sent, run,
                s => StopsAt(s, cellId, 3, "step"), Patience, $"a step stop on line 3 of cell {cellId}");
            var assigned = VariablesOf(afterStep);
            Assert.AreEqual("1", assigned["a"]);
            Assert.AreEqual("42", assigned["b"]);
            Assert.IsFalse(run.IsCompleted, "the run holds at the step too");

            var continued = await DispatchAsync(session, 5, MethodNames.DebugResume, new { notebookId, action = "continue" });
            Assert.IsTrue(ResultOf(continued, "debug/resume continue").GetProperty("resumed").GetBoolean());

            var result = ResultOf(await WaitForRunAsync(run, "the run to finish after continue"), "execution/run");
            Assert.AreEqual("completed", result.GetProperty("status").GetString(), result.ToString());
            Assert.IsTrue(result.GetProperty("outputs").EnumerateArray()
                    .Any(o => o.GetProperty("content").GetString()!.Contains("c = 84")),
                $"expected the cell's output, got: {result.GetProperty("outputs")}");

            // The client goes by the last state it heard, so that has to say nothing is stopped.
            var last = sent.DebugStates().Last();
            Assert.IsFalse(HasStop(last), $"the last state still shows a stop: {last}");
            Assert.IsTrue(last.GetProperty("enabled").GetBoolean());

            // Nothing is left to resume.
            var after = await DispatchAsync(session, 6, MethodNames.DebugResume, new { notebookId, action = "continue" });
            Assert.IsFalse(ResultOf(after, "debug/resume after the run").GetProperty("resumed").GetBoolean());
        }
    }

    [TestMethod]
    public async Task CancellingAStoppedCell_EndsTheRunAsCancelled()
    {
        var (session, notebookId, sent) = await OpenAsync();
        await using (session)
        {
            var code = "var a = 1;\nConsole.WriteLine(\"ran on\");";
            var (cellId, run) = await StartStoppedRunAsync(session, notebookId, code, breakpointLine: 2);

            await WaitForStopAsync(sent, run,
                s => StopsAt(s, cellId, 2, "breakpoint"), FirstStopPatience,
                $"a stop at the breakpoint on line 2 of cell {cellId}");

            var cancel = await DispatchAsync(session, 4, MethodNames.ExecutionCancel, new { notebookId });
            ResultOf(cancel, "execution/cancel");

            var result = ResultOf(await WaitForRunAsync(run, "the run to end after cancel"), "execution/run");
            Assert.AreEqual("cancelled", result.GetProperty("status").GetString(), result.ToString());
            Assert.IsFalse(result.GetProperty("outputs").EnumerateArray()
                    .Any(o => o.GetProperty("content").GetString()!.Contains("ran on")),
                "the line after the breakpoint must not run");

            var last = sent.DebugStates().Last();
            Assert.IsFalse(HasStop(last), $"the last state still shows a stop: {last}");

            var after = await DispatchAsync(session, 5, MethodNames.DebugResume, new { notebookId, action = "continue" });
            Assert.IsFalse(ResultOf(after, "debug/resume after cancel").GetProperty("resumed").GetBoolean());
        }
    }

    [TestMethod]
    public async Task TurningDebuggingOffWhileStopped_LetsTheCellRunToItsEnd()
    {
        var (session, notebookId, sent) = await OpenAsync();
        await using (session)
        {
            var (cellId, run) = await StartStoppedRunAsync(session, notebookId, Code, breakpointLine: 2);

            await WaitForStopAsync(sent, run,
                s => StopsAt(s, cellId, 2, "breakpoint"), FirstStopPatience,
                $"a stop at the breakpoint on line 2 of cell {cellId}");

            var off = await DispatchAsync(session, 4, MethodNames.DebugSetEnabled, new { notebookId, enabled = false });
            Assert.IsFalse(ResultOf(off, "debug/setEnabled false").GetProperty("enabled").GetBoolean());

            var result = ResultOf(await WaitForRunAsync(run, "the run to finish with debugging off"), "execution/run");
            Assert.AreEqual("completed", result.GetProperty("status").GetString(), result.ToString());

            var last = sent.DebugStates().Last();
            Assert.IsFalse(HasStop(last), $"the last state still shows a stop: {last}");
            Assert.IsFalse(last.GetProperty("enabled").GetBoolean());
        }
    }

    // --- through the host process ---

    /// <summary>
    /// The same stop through the real host, where requests are read on one thread and handled
    /// one at a time on another. Only the inline rule in the reader lets the resume reach a cell
    /// whose run is the request the main loop is busy with.
    /// </summary>
    [TestMethod]
    public async Task HostProcess_AStoppedCellIsResumedWhileItsRunIsStillOutstanding()
    {
        using var process = StartHost(out var sent, out var pump);

        try
        {
            var (notebookId, _) = await StopACellAsync(process, sent);

            await SendAsync(process, 6, MethodNames.DebugResume, new { notebookId, action = "continue" });
            var resumed = await WaitForResponseAsync(sent, 6, "debug/resume");
            Assert.IsTrue(ResultOf(resumed, "debug/resume").GetProperty("resumed").GetBoolean());

            var run = ResultOf(await WaitForResponseAsync(sent, 5, "execution/run after continue"), "execution/run");
            Assert.AreEqual("completed", run.GetProperty("status").GetString(), run.ToString());

            // The reader answers the resume and the cell's own thread announces that it moved on,
            // so the two can reach the stream in either order. What matters is that the client
            // ends up hearing nothing is stopped.
            await WaitForAsync(() => !HasStop(sent.DebugStates().Last()),
                Patience, "a final state with no stop");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
            await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }

    /// <summary>
    /// A cell stopped at a breakpoint waits for an answer only its client can give. When the
    /// client goes away without one, the host has to let the cell go, or it would sit in that
    /// run for good and be left behind as a process nobody owns.
    /// </summary>
    [TestMethod]
    public async Task HostProcess_ExitsWhenItsClientGoesAwayWhileACellIsStopped()
    {
        using var process = StartHost(out var sent, out var pump);

        try
        {
            await StopACellAsync(process, sent);

            process.StandardInput.Close();

            using var patience = new CancellationTokenSource(Patience);
            try
            {
                await process.WaitForExitAsync(patience.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"The host was still running {Patience.TotalSeconds:0}s after its input closed with a cell stopped.");
            }
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
            await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }

    private static Process StartHost(out Sent sent, out Task pump)
    {
        var hostDll = FindHostDll();
        if (hostDll is null)
            Assert.Inconclusive("Verso.Host.dll not found in build output");

        var process = Process.Start(new ProcessStartInfo("dotnet", hostDll)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        var messages = new Sent();
        sent = messages;
        pump = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
            {
                if (line.TrimStart().StartsWith("{", StringComparison.Ordinal))
                    messages.Add(line);
            }
        });
        _ = Task.Run(async () =>
        {
            // Drained so a chatty host cannot fill the pipe and stall.
            while (await process.StandardError.ReadLineAsync() is not null) { }
        });

        return process;
    }

    // Opens a notebook in the host, runs a cell with a breakpoint on its second line, and
    // returns once the host has said the cell is stopped there. The run is request 5.
    private static async Task<(string NotebookId, string CellId)> StopACellAsync(Process process, Sent sent)
    {
        await WaitForAsync(() => sent.Messages().Any(m =>
                m.TryGetProperty("method", out var method) && method.GetString() == MethodNames.HostReady),
            Patience, "host/ready");

        await SendAsync(process, 1, MethodNames.NotebookOpen, new { content = "" });
        var open = await WaitForResponseAsync(sent, 1, "notebook/open");
        var notebookId = ResultOf(open, "notebook/open").GetProperty("notebookId").GetString()!;

        await SendAsync(process, 2, MethodNames.CellAdd, new { notebookId, type = "code", language = "csharp", source = Code });
        var cellId = ResultOf(await WaitForResponseAsync(sent, 2, "cell/add"), "cell/add").GetProperty("id").GetString()!;

        await SendAsync(process, 3, MethodNames.DebugSetEnabled, new { notebookId, enabled = true });
        ResultOf(await WaitForResponseAsync(sent, 3, "debug/setEnabled"), "debug/setEnabled");
        await SendAsync(process, 4, MethodNames.DebugSetBreakpoints, new { notebookId, cellId, lines = new[] { 2 } });
        ResultOf(await WaitForResponseAsync(sent, 4, "debug/setBreakpoints"), "debug/setBreakpoints");

        await SendAsync(process, 5, MethodNames.ExecutionRun, new { notebookId, cellId });
        await WaitForAsync(() => sent.HasDebugState(s => StopsAt(s, cellId, 2, "breakpoint")),
            FirstStopPatience, $"a stop on line 2 of cell {cellId} from the host process");
        Assert.IsNull(sent.Response(5), "the run must not have answered while its cell is stopped");

        return (notebookId, cellId);
    }

    private static string? FindHostDll()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var searchDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));

        // Several copies can coexist; the newest is the one this run just built.
        return Directory.GetFiles(searchDir, "Verso.Host.dll", SearchOption.AllDirectories)
            .Where(p => p.Contains(Path.Combine("Verso.Host", "bin")) && !p.Contains("Tests"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static async Task SendAsync(Process process, int id, string method, object @params)
    {
        if (process.HasExited)
            Assert.Fail($"Host process exited with code {process.ExitCode} before {method} could be sent");

        var json = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id, method, @params }, JsonRpcMessage.SerializerOptions);
        await process.StandardInput.WriteLineAsync(json);
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonElement> WaitForResponseAsync(Sent sent, long id, string what)
    {
        await WaitForAsync(() => sent.Response(id) is not null,
            id == 1 ? FirstStopPatience : Patience, $"the response to {what}");
        return sent.Response(id)!.Value;
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan patience, string waitingFor)
    {
        var deadline = DateTime.UtcNow + patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out after {patience.TotalSeconds:0}s waiting for {waitingFor}.");
            await Task.Delay(20);
        }
    }
}
