using Verso.Contexts;
using Verso.Debugging;
using Verso.Execution;
using Verso.Kernels;
using Verso.Testing.Fakes;

namespace Verso.Tests.Debugging;

[TestClass]
public sealed class NotebookDebuggerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [TestMethod]
    public void SetBreakpoints_KeepsThemSorted_AndIgnoresLinesBelowOne()
    {
        var debugger = new NotebookDebugger();
        var cell = Guid.NewGuid();

        debugger.SetBreakpoints(cell, new[] { 7, 0, 3, -2, 7 });

        CollectionAssert.AreEqual(new[] { 3, 7 }, debugger.GetBreakpoints(cell).ToArray());
        CollectionAssert.AreEqual(new[] { cell }, debugger.GetAllBreakpoints().Keys.ToArray());
    }

    [TestMethod]
    public void SetBreakpoints_AnnouncesOnlyARealChange()
    {
        var debugger = new NotebookDebugger();
        var cell = Guid.NewGuid();
        var changes = 0;
        debugger.Changed += () => changes++;

        debugger.SetBreakpoints(cell, new[] { 2 });
        debugger.SetBreakpoints(cell, new[] { 2 });
        debugger.SetBreakpoints(Guid.NewGuid(), Array.Empty<int>());
        Assert.AreEqual(1, changes);

        debugger.SetBreakpoints(cell, Array.Empty<int>());
        Assert.AreEqual(2, changes);
        Assert.AreEqual(0, debugger.GetAllBreakpoints().Count);
    }

    [TestMethod]
    public void AKernelSeesNoBreakpoints_WhileDebuggingIsOff()
    {
        var debugger = new NotebookDebugger();
        var cell = Guid.NewGuid();
        debugger.SetBreakpoints(cell, new[] { 4 });

        using var session = debugger.BeginRun(cell, lineOffset: 0, CancellationToken.None);
        var heard = 0;
        session.BreakpointsChanged += () => heard++;

        Assert.AreEqual(0, session.GetBreakpoints(cell).Count);

        debugger.Enabled = true;
        Assert.AreEqual(1, heard, "Turning debugging on changes what a kernel should stop at.");
        CollectionAssert.AreEqual(new[] { 4 }, session.GetBreakpoints(cell).ToArray());
    }

    [TestMethod]
    public void AKernelSeesBreakpoints_InTheLinesOfTheCodeItWasGiven()
    {
        var debugger = new NotebookDebugger { Enabled = true };
        var cell = Guid.NewGuid();
        debugger.SetBreakpoints(cell, new[] { 1, 2, 3, 5 });

        // Two lines of the cell were consumed before the kernel saw the rest.
        using var session = debugger.BeginRun(cell, lineOffset: 2, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { 1, 3 }, session.GetBreakpoints(cell).ToArray());
    }

    [TestMethod]
    public async Task AStop_IsShownInTheCellsOwnLines_UntilTheUserResumes()
    {
        var debugger = new NotebookDebugger { Enabled = true };
        var cell = Guid.NewGuid();
        using var session = debugger.BeginRun(cell, lineOffset: 2, CancellationToken.None);

        var stopped = WaitForChange(debugger, d => d.CurrentStop is not null);
        var waiting = session.StopAsync(Stop(cell, line: 3));
        await stopped.WaitAsync(Patience);

        var shown = debugger.CurrentStop!;
        Assert.AreEqual(DebugStopReason.Breakpoint, shown.Reason);
        Assert.AreEqual(5, shown.Frames[0].Line);
        Assert.IsFalse(waiting.IsCompleted);

        Assert.IsTrue(debugger.Resume(DebugResumeAction.StepOver));
        Assert.AreEqual(DebugResumeAction.StepOver, await waiting.WaitAsync(Patience));
        Assert.IsNull(debugger.CurrentStop);
        Assert.IsFalse(debugger.Resume(DebugResumeAction.Continue), "Nothing is stopped any more.");
    }

    [TestMethod]
    public async Task AFrameInAnotherCell_IsTranslatedByThatCellsOwnOffset()
    {
        var debugger = new NotebookDebugger { Enabled = true };
        var earlier = Guid.NewGuid();
        var running = Guid.NewGuid();
        debugger.BeginRun(earlier, lineOffset: 1, CancellationToken.None).Dispose();
        using var session = debugger.BeginRun(running, lineOffset: 0, CancellationToken.None);

        var stop = new DebugStop(DebugStopReason.Step, new[]
        {
            new DebugFrame("Model", earlier, 3, Array.Empty<DebugVariable>()),
            new DebugFrame("", running, 2, Array.Empty<DebugVariable>()),
            new DebugFrame("library", null, 40, Array.Empty<DebugVariable>()),
        });

        var waiting = session.StopAsync(stop);
        await WaitUntil(() => debugger.CurrentStop is not null);

        CollectionAssert.AreEqual(new[] { 4, 2, 40 }, debugger.CurrentStop!.Frames.Select(f => f.Line).ToArray());
        debugger.Resume(DebugResumeAction.Continue);
        await waiting.WaitAsync(Patience);
    }

    [TestMethod]
    public async Task TurningDebuggingOff_LetsAStoppedCellRunOn()
    {
        var debugger = new NotebookDebugger { Enabled = true };
        var cell = Guid.NewGuid();
        using var session = debugger.BeginRun(cell, lineOffset: 0, CancellationToken.None);

        var waiting = session.StopAsync(Stop(cell, line: 1));
        await WaitUntil(() => debugger.CurrentStop is not null);

        debugger.Enabled = false;

        Assert.AreEqual(DebugResumeAction.Continue, await waiting.WaitAsync(Patience));
        Assert.IsNull(debugger.CurrentStop);

        // And a kernel that reaches another stop before it hears is waved straight through.
        Assert.AreEqual(DebugResumeAction.Continue, await session.StopAsync(Stop(cell, line: 2)).WaitAsync(Patience));
    }

    [TestMethod]
    public async Task CancellingTheRun_EndsItsStop()
    {
        var debugger = new NotebookDebugger { Enabled = true };
        var cell = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        using var session = debugger.BeginRun(cell, lineOffset: 0, cts.Token);

        var waiting = session.StopAsync(Stop(cell, line: 1));
        await WaitUntil(() => debugger.CurrentStop is not null);

        cts.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => waiting.WaitAsync(Patience));
        Assert.IsNull(debugger.CurrentStop);
    }

    [TestMethod]
    public async Task ASecondStop_WaitsForTheFirstToBeAnswered()
    {
        var debugger = new NotebookDebugger { Enabled = true };
        var cell = Guid.NewGuid();
        using var session = debugger.BeginRun(cell, lineOffset: 0, CancellationToken.None);

        var first = session.StopAsync(Stop(cell, line: 1));
        await WaitUntil(() => debugger.CurrentStop is not null);
        var second = session.StopAsync(Stop(cell, line: 9));

        Assert.AreEqual(1, debugger.CurrentStop!.Frames[0].Line);

        debugger.Resume(DebugResumeAction.Continue);
        await first.WaitAsync(Patience);
        await WaitUntil(() => debugger.CurrentStop?.Frames[0].Line == 9);

        debugger.Resume(DebugResumeAction.Continue);
        await second.WaitAsync(Patience);
    }

    [TestMethod]
    public void ForgettingACell_DropsItsBreakpoints()
    {
        var debugger = new NotebookDebugger();
        var cell = Guid.NewGuid();
        debugger.SetBreakpoints(cell, new[] { 1 });

        debugger.ForgetCell(cell);

        Assert.AreEqual(0, debugger.GetBreakpoints(cell).Count);
    }

    [TestMethod]
    public void LinesBefore_CountsWhatWasConsumedFromTheTopOfTheCell()
    {
        Assert.AreEqual(0, ExecutionPipeline.LinesBefore("var a = 1;", "var a = 1;"));
        Assert.AreEqual(1, ExecutionPipeline.LinesBefore("#!time\nvar a = 1;", "var a = 1;"));
        Assert.AreEqual(2, ExecutionPipeline.LinesBefore("#!time\r\n\r\nvar a = 1;", "var a = 1;"));
        Assert.AreEqual(0, ExecutionPipeline.LinesBefore("something else", "var a = 1;"));
        Assert.AreEqual(0, ExecutionPipeline.LinesBefore(null, "var a = 1;"));
    }

    // ------------------------------------------------------------------ through a notebook

    [TestMethod]
    public async Task ADebuggedCSharpCell_StopsAtItsBreakpoint_AndRunsOnWhenResumed()
    {
        await using var scaffold = new Verso.Scaffold();
        scaffold.DefaultKernelId = "csharp";
        scaffold.RegisterKernel(new CSharpKernel());

        var cell = scaffold.AddCell(source: "var a = 1;\nvar b = a + 41;\nb");
        Assert.IsTrue(scaffold.CanDebugCell(cell.Id));

        scaffold.Debugger.Enabled = true;
        scaffold.Debugger.SetBreakpoints(cell.Id, new[] { 2 });

        var running = Task.Run(() => scaffold.ExecuteCellAsync(cell.Id));
        await WaitUntil(() => scaffold.Debugger.CurrentStop is not null);

        var stop = scaffold.Debugger.CurrentStop!;
        Assert.AreEqual(cell.Id, stop.Frames[0].CellId);
        Assert.AreEqual(2, stop.Frames[0].Line);
        Assert.AreEqual("1", stop.Frames[0].Variables.Single(v => v.Name == "a").Value);
        Assert.IsFalse(running.IsCompleted, "The cell holds still while it is stopped.");

        scaffold.Debugger.Resume(DebugResumeAction.Continue);
        var result = await running.WaitAsync(Patience);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
        Assert.AreEqual("42", cell.Outputs[^1].Content);
        Assert.IsNull(scaffold.Debugger.CurrentStop);
    }

    [TestMethod]
    public async Task CancellingAStoppedCell_ReportsItCancelled()
    {
        await using var scaffold = new Verso.Scaffold();
        scaffold.DefaultKernelId = "csharp";
        scaffold.RegisterKernel(new CSharpKernel());
        var cell = scaffold.AddCell(source: "var a = 1;\nConsole.WriteLine(\"ran on\");");

        scaffold.Debugger.Enabled = true;
        scaffold.Debugger.SetBreakpoints(cell.Id, new[] { 2 });

        using var cts = new CancellationTokenSource();
        var running = Task.Run(() => scaffold.ExecuteCellAsync(cell.Id, cts.Token));
        await WaitUntil(() => scaffold.Debugger.CurrentStop is not null);

        cts.Cancel();
        var result = await running.WaitAsync(Patience);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Cancelled, result.Status);
        Assert.IsNull(scaffold.Debugger.CurrentStop);
        Assert.IsFalse(cell.Outputs.Any(o => o.Content.Contains("ran on")));
    }

    [TestMethod]
    public async Task AKernelThatCannotPause_RunsAsUsualWithDebuggingOn()
    {
        await using var scaffold = new Verso.Scaffold();
        scaffold.DefaultKernelId = "fake";
        scaffold.RegisterKernel(new FakeLanguageKernel("fake"));
        var cell = scaffold.AddCell(source: "hello");

        scaffold.Debugger.Enabled = true;
        scaffold.Debugger.SetBreakpoints(cell.Id, new[] { 1 });

        Assert.IsFalse(scaffold.CanDebugCell(cell.Id));
        var result = await scaffold.ExecuteCellAsync(cell.Id).WaitAsync(Patience);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
        Assert.AreEqual("Executed: hello", cell.Outputs[0].Content);
    }

    [TestMethod]
    public async Task ThePipeline_TellsTheKernelsSessionHowFarDownTheCellItsCodeStarts()
    {
        var kernel = new StoppingKernel(lineInCode: 2);
        var debugger = new NotebookDebugger { Enabled = true };
        var magic = new PassThroughMagicCommand();

        var pipeline = new ExecutionPipeline(
            new VariableStore(),
            new Verso.Stubs.StubThemeContext(),
            LayoutCapabilities.CellExecute,
            new Verso.Stubs.StubExtensionHostContext(() => new ILanguageKernel[] { kernel }),
            new NotebookMetadataContext(new NotebookModel()),
            new Verso.Stubs.StubNotebookOperations(),
            _ => kernel,
            _ => Task.CompletedTask,
            _ => kernel.LanguageId,
            _ => 1,
            name => name == magic.Name ? magic : null,
            debugger: debugger);

        var cell = new CellModel { Type = "code", Language = "stopping", Source = "#!passthrough\n\nfirst\nsecond" };
        debugger.SetBreakpoints(cell.Id, new[] { 4 });

        var running = Task.Run(() => pipeline.ExecuteAsync(cell, CancellationToken.None));
        await WaitUntil(() => debugger.CurrentStop is not null);

        // The kernel was given the two lines below the magic command and reported its line 2.
        Assert.AreEqual("\nfirst\nsecond", kernel.CodeSeen);
        CollectionAssert.AreEqual(new[] { 3 }, kernel.BreakpointsSeen);
        Assert.AreEqual(3, debugger.CurrentStop!.Frames[0].Line);

        debugger.Resume(DebugResumeAction.Continue);
        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, (await running.WaitAsync(Patience)).Status);
    }

    [TestMethod]
    public async Task CodeRunOnTheNotebooksBehalf_IsNeverStopped()
    {
        await using var scaffold = new Verso.Scaffold();
        scaffold.DefaultKernelId = "csharp";
        scaffold.RegisterKernel(new CSharpKernel());
        var cell = scaffold.AddCell(source: "int Twice(int n)\n{\n    var doubled = n * 2;\n    return doubled;\n}");

        scaffold.Debugger.Enabled = true;
        await scaffold.ExecuteCellAsync(cell.Id).WaitAsync(Patience);
        scaffold.Debugger.SetBreakpoints(cell.Id, new[] { 3 });

        // Not a cell the user ran, so there is nowhere on screen to show a stop.
        var outputs = await scaffold.ExecuteCodeCaptureOutputsAsync("Twice(21)", "csharp").WaitAsync(Patience);

        Assert.AreEqual("42", outputs[^1].Content);
        Assert.IsNull(scaffold.Debugger.CurrentStop);
    }

    [TestMethod]
    public async Task DisposingTheNotebook_LetsAStoppedCellGo()
    {
        var scaffold = new Verso.Scaffold();
        scaffold.DefaultKernelId = "csharp";
        scaffold.RegisterKernel(new CSharpKernel());
        var cell = scaffold.AddCell(source: "var a = 1;\nvar b = 2;");

        scaffold.Debugger.Enabled = true;
        scaffold.Debugger.SetBreakpoints(cell.Id, new[] { 2 });
        var running = Task.Run(() => scaffold.ExecuteCellAsync(cell.Id));
        await WaitUntil(() => scaffold.Debugger.CurrentStop is not null);

        await scaffold.DisposeAsync().AsTask().WaitAsync(Patience);
        await running.WaitAsync(Patience);
    }

    private static DebugStop Stop(Guid cell, int line)
        => new(DebugStopReason.Breakpoint, new[] { new DebugFrame("", cell, line, Array.Empty<DebugVariable>()) });

    private static Task WaitForChange(NotebookDebugger debugger, Func<NotebookDebugger, bool> condition)
    {
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        debugger.Changed += () =>
        {
            if (condition(debugger)) seen.TrySetResult();
        };
        return seen.Task;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Timed out waiting for the debugger.");
            await Task.Delay(10);
        }
    }

    /// <summary>A kernel that stops once, at a fixed line of whatever code it is given.</summary>
    private sealed class StoppingKernel : ILanguageKernel, IDebuggableKernel
    {
        private readonly int _lineInCode;

        public StoppingKernel(int lineInCode) => _lineInCode = lineInCode;

        public string? CodeSeen { get; private set; }
        public int[] BreakpointsSeen { get; private set; } = Array.Empty<int>();

        public string ExtensionId => "com.test.stopping";
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
            => throw new InvalidOperationException("With debugging on, a kernel that can pause is asked to.");

        public async Task<IReadOnlyList<CellOutput>> DebugAsync(string code, IExecutionContext context, IDebugSession session)
        {
            CodeSeen = code;
            BreakpointsSeen = session.GetBreakpoints(context.CellId).OrderBy(l => l).ToArray();
            await session.StopAsync(new DebugStop(
                DebugStopReason.Breakpoint,
                new[] { new DebugFrame("", context.CellId, _lineInCode, Array.Empty<DebugVariable>()) }));
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

    private sealed class PassThroughMagicCommand : IMagicCommand
    {
        public string ExtensionId => "test.passthrough";
        public string Name => "passthrough";
        public string Version => "0.1.0";
        public string? Author => null;
        public string Description => "Consumes its own line and lets the rest run.";
        public IReadOnlyList<ParameterDefinition> Parameters => Array.Empty<ParameterDefinition>();

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
        public Task ExecuteAsync(string arguments, IMagicCommandContext context) => Task.CompletedTask;
    }
}
