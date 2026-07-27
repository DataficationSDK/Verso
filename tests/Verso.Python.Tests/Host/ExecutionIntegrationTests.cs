using System.Diagnostics;
using Verso.Abstractions;
using Verso.Python.Host;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.Tests.Interpreter;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Exercises the execution pipeline against a real Python subprocess: streaming, results, errors,
/// interactive input, interrupt escalation, and crash recovery. Interpreter discovery is stubbed so
/// these tests measure execution rather than the search, and so a system interpreter carrying a PEP
/// 668 marker does not pull environment creation into every run.
/// </summary>
[TestClass]
public sealed class ExecutionIntegrationTests
{
    private static string Python => PythonProbe.Require();

    private static PythonHostSession CreateSession(PythonKernelOptions? options = null)
    {
        var python = Python;
        var validator = new FakeInterpreterValidator().Valid(python, version: "3.13.0");
        var locator = new InterpreterLocator(validator, _ => null, () => Array.Empty<string>());

        options = (options ?? new PythonKernelOptions()) with { PythonExecutable = python };
        return new PythonHostSession(options, locator: locator);
    }

    private static async Task<PythonHostSession> StartedSessionAsync(PythonKernelOptions? options = null)
    {
        var session = CreateSession(options);
        await session.StartAsync(CancellationToken.None);
        return session;
    }

    private static string TextOf(IEnumerable<CellOutput> outputs)
        => string.Concat(outputs.Where(o => o.MimeType == "text/plain").Select(o => o.Content));

    // --- results and scope ---

    [TestMethod]
    public async Task Execute_ReturnsTheLastExpression()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        var outputs = await session.ExecuteAsync("1 + 1", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("text/plain", outputs[0].MimeType);
        Assert.AreEqual("2", outputs[0].Content);
        Assert.IsFalse(outputs[0].IsError);
    }

    [TestMethod]
    public async Task Execute_HandlesAMultiLineTrailingExpression()
    {
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync("(1 +\n 2 +\n 3)", new StubExecutionContext());

        Assert.AreEqual("6", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_TrailingSemicolonSuppressesTheResult()
    {
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync("40 + 2;", new StubExecutionContext());

        Assert.AreEqual(0, outputs.Count);
    }

    [TestMethod]
    public async Task Execute_KeepsScopeAcrossCells()
    {
        await using var session = await StartedSessionAsync();

        await session.ExecuteAsync("saved = 41", new StubExecutionContext());
        var outputs = await session.ExecuteAsync("saved + 1", new StubExecutionContext());

        Assert.AreEqual("42", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_SupportsTopLevelAwait()
    {
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync(
            "import asyncio\nawait asyncio.sleep(0.01)\n'awaited'", new StubExecutionContext());

        Assert.AreEqual("'awaited'", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_AppliesBootstrapImportsAndStartupCode()
    {
        var options = new PythonKernelOptions
        {
            // The unavailable module has to be skipped rather than failing the bootstrap.
            DefaultImports = new[] { "json", "verso_module_that_does_not_exist" },
            StartupCode = "bootstrapped = 'yes'",
        };
        await using var session = await StartedSessionAsync(options);

        var outputs = await session.ExecuteAsync(
            "bootstrapped + json.dumps([1])", new StubExecutionContext());

        Assert.AreEqual("'yes[1]'", outputs[0].Content);
    }

    // --- streaming ---

    [TestMethod]
    public async Task Execute_WritesStreamOutputThroughTheContext()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        var outputs = await session.ExecuteAsync("print('one')\nprint('two')", context);

        StringAssert.Contains(TextOf(outputs), "one");
        StringAssert.Contains(TextOf(outputs), "two");

        // The same instances must reach the host and be returned, so the execution pipeline
        // recognizes them as already streamed and does not add a second copy to the cell.
        // Running text is revised in place rather than appended, so an output can arrive either
        // way and both count.
        var seen = context.WrittenOutputs
            .Concat(context.UpdatedOutputs.Select(u => u.Output))
            .ToList();

        Assert.IsTrue(
            outputs.All(o => seen.Any(s => ReferenceEquals(s, o))),
            "returned outputs must be the instances that reached the host during execution");
    }

    /// <summary>
    /// A progress bar redraws its line by writing a carriage return and the line again. Appended
    /// verbatim that is one output per redraw; interpreted as a terminal would, it is one line
    /// that changes.
    /// </summary>
    [TestMethod]
    public async Task Execute_RedrawnLine_ProducesOneRevisedOutputRatherThanMany()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        var code =
            "import sys, time\n" +
            "for i in range(5):\n" +
            "    sys.stdout.write('\\rstep %d' % i)\n" +
            "    sys.stdout.flush()\n" +
            "    time.sleep(0.06)\n" +
            "sys.stdout.write('\\n')\n";

        var outputs = await session.ExecuteAsync(code, context);

        // Every redraw revised the same block, so the cell ends with one stream output showing the
        // last state of the line rather than five showing each state. Filtered by channel because
        // the cell's final expression value is an output too: write() returns a character count.
        var streamed = outputs.Where(o => o.Channel == OutputChannel.Stdout).ToList();
        Assert.AreEqual(1, streamed.Count, $"expected a single revised output, got: {TextOf(outputs)}");
        StringAssert.Contains(streamed[0].Content, "step 4");
        Assert.IsFalse(streamed[0].Content.Contains("step 0"), "an overwritten redraw must not linger");
    }

    [TestMethod]
    public async Task Execute_LongOutput_RollsOntoFurtherBlocksInsteadOfOneGrowingBlock()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        // Revising a block sends everything it holds, so one block that only ever grows makes each
        // chunk cost more than the last. Past a point the text rolls onto another block and the
        // cost stays flat. Nothing may be lost in the handover.
        var code =
            "line = 'x' * 1000\n" +
            "for i in range(400):\n" +
            "    print(line)\n";

        var outputs = await session.ExecuteAsync(code, context);

        var streamed = outputs.Where(o => o.Channel == OutputChannel.Stdout).ToList();
        Assert.IsTrue(streamed.Count > 1, "400 KB of output should not sit in a single block.");

        var combined = string.Concat(streamed.Select(o => o.Content));
        Assert.AreEqual(400, combined.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length,
            "every line written must survive the handover between blocks");
    }

    /// <summary>
    /// Writing to standard error is how a great many working programs report progress and
    /// warnings, so it is recorded as its own channel and must not fail the cell.
    /// </summary>
    [TestMethod]
    public async Task Execute_RecordsStandardErrorAsItsOwnChannelWithoutFailingTheCell()
    {
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync(
            "import sys\nsys.stderr.write('careful\\n');", new StubExecutionContext());

        var stderr = outputs.SingleOrDefault(o => o.Channel == OutputChannel.Stderr);
        Assert.IsNotNull(stderr, "standard error should surface as its own channel");
        StringAssert.Contains(stderr!.Content, "careful");
        Assert.IsFalse(outputs.Any(o => o.IsError), "writing to standard error is not a failure");
    }

    /// <summary>
    /// An exception on a thread the cell started never reaches the reply that reports the cell's
    /// result, so without the host's own hook it would arrive as ordinary text and the notebook
    /// would look like it succeeded.
    /// </summary>
    [TestMethod]
    public async Task Execute_ReportsAnExceptionRaisedOnABackgroundThread()
    {
        await using var session = await StartedSessionAsync();

        var code =
            "import threading\n" +
            "\n" +
            "def boom():\n" +
            "    raise ValueError('background failure')\n" +
            "\n" +
            "t = threading.Thread(target=boom)\n" +
            "t.start()\n" +
            "t.join()\n";

        var outputs = await session.ExecuteAsync(code, new StubExecutionContext());

        var failure = outputs.SingleOrDefault(o => o.IsError);
        Assert.IsNotNull(failure, "a background thread failure should fail the cell");
        StringAssert.Contains(failure!.Content, "background failure");
        Assert.AreEqual("ValueError", failure.ErrorName);
    }

    [TestMethod]
    public async Task Execute_StreamsOutputBeforeTheCellCompletes()
    {
        await using var session = await StartedSessionAsync();

        var firstOutput = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = Stopwatch.StartNew();
        var context = new StreamTimingContext(_ => firstOutput.TrySetResult(stopwatch.Elapsed));

        // Each iteration prints and then sleeps, so buffered-until-the-end output would not
        // arrive until well after the first chunk is observable.
        await session.ExecuteAsync(
            "import time\nfor i in range(3):\n    print(i)\n    time.sleep(0.4)", context);

        var total = stopwatch.Elapsed;
        Assert.IsTrue(firstOutput.Task.IsCompleted, "no output arrived during execution");

        var first = await firstOutput.Task;
        Assert.IsTrue(
            first < total - TimeSpan.FromMilliseconds(500),
            $"output should stream during the cell, but the first chunk arrived at {first} of {total}");
    }

    // --- rich display ---

    [TestMethod]
    public async Task Execute_DisplayEmitsBeforeTheCellEnds()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        var code =
            "class Card:\n" +
            "    def _repr_html_(self):\n" +
            "        return '<b>card</b>'\n" +
            "display(Card())\n" +
            "print('after')";

        var outputs = await session.ExecuteAsync(code, context);

        // The display arrives where the call was made, ahead of the print that follows it.
        Assert.AreEqual("text/html", outputs[0].MimeType);
        Assert.AreEqual("<b>card</b>", outputs[0].Content);
        StringAssert.Contains(TextOf(outputs), "after");
    }

    [TestMethod]
    public async Task Execute_DisplaysAnImageAsAnElement()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        var code =
            "class Picture:\n" +
            "    def _repr_png_(self):\n" +
            "        return b'\\x89PNG\\r\\n\\x1a\\n'\n" +
            "Picture()";

        var outputs = await session.ExecuteAsync(code, context);

        Assert.AreEqual("text/html", outputs[^1].MimeType);
        StringAssert.Contains(outputs[^1].Content, "data:image/png;base64,");
    }

    [TestMethod]
    public async Task Execute_ProvidesTheDisplayNamesWithoutIPythonInstalled()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        var outputs = await session.ExecuteAsync(
            "from IPython.display import HTML, clear_output\nclear_output()\nHTML('<i>shim</i>')",
            context);

        Assert.AreEqual("text/html", outputs[^1].MimeType);
        Assert.AreEqual("<i>shim</i>", outputs[^1].Content);
    }

    // --- errors ---

    [TestMethod]
    public async Task Execute_ReportsSyntaxErrorsWithCaretContext()
    {
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync("def (:", new StubExecutionContext());

        var error = outputs.Single(o => o.IsError);
        Assert.AreEqual("SyntaxError", error.ErrorName);
        StringAssert.Contains(error.Content, "^");
    }

    [TestMethod]
    public async Task Execute_ReportsRuntimeErrorsWithTheUserTraceback()
    {
        await using var session = await StartedSessionAsync();

        var outputs = await session.ExecuteAsync(
            "def boom():\n    raise ValueError('nope')\nboom()", new StubExecutionContext());

        var error = outputs.Single(o => o.IsError);
        Assert.AreEqual("ValueError", error.ErrorName);
        StringAssert.Contains(error.Content, "nope");
        StringAssert.Contains(error.Content, "in boom");

        // The frames that run the cell belong to the harness and must not appear.
        Assert.IsFalse(error.Content.Contains("versohost.py"), "host frames leaked into the traceback");
    }

    // --- interactive input ---

    [TestMethod]
    public async Task Execute_RoundTripsInteractiveInput()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext
        {
            InputHandler = (prompt, isPassword, _) =>
            {
                Assert.AreEqual("Name: ", prompt);
                Assert.IsFalse(isPassword);
                return Task.FromResult<string?>("Ada");
            },
        };

        var outputs = await session.ExecuteAsync("name = input('Name: ')\nname", context);

        Assert.AreEqual("'Ada'", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_UnsupportedInputRaisesEndOfFile()
    {
        await using var session = await StartedSessionAsync();

        // No handler on the stub, so the host reports interactive input as unsupported, which is
        // what a non-interactive interpreter does.
        var outputs = await session.ExecuteAsync("input('anything')", new StubExecutionContext());

        Assert.AreEqual("EOFError", outputs.Single(o => o.IsError).ErrorName);
    }

    // --- cancellation ---

    [TestMethod]
    public async Task Cancel_InterruptsABusyLoopAndKeepsTheInterpreter()
    {
        await using var session = await StartedSessionAsync();

        using var cancellation = new CancellationTokenSource();
        var context = new StubExecutionContext { CancellationToken = cancellation.Token };
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(500));

        var stopwatch = Stopwatch.StartNew();
        await session.ExecuteAsync("marker = 'before'\nwhile True:\n    pass", context);
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"the loop should stop on the interrupt, not the escalation timer (took {stopwatch.Elapsed})");

        // Interpreter state survives an interrupt that the signal alone resolved.
        var outputs = await session.ExecuteAsync("marker", new StubExecutionContext());
        Assert.AreEqual("'before'", outputs[0].Content);
    }

    [TestMethod]
    public async Task Cancel_StopsASignalProofCellAndRecovers()
    {
        // A short grace period keeps the escalation observable without a long test.
        await using var session = await StartedSessionAsync(
            new PythonKernelOptions { InterruptGracePeriod = TimeSpan.FromSeconds(1) });

        using var cancellation = new CancellationTokenSource();
        var context = new StubExecutionContext { CancellationToken = cancellation.Token };
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(500));

        var outputs = await session.ExecuteAsync(
            "import signal, time\n" +
            "signal.signal(signal.SIGINT, signal.SIG_IGN)\n" +
            "time.sleep(30)", context);

        StringAssert.Contains(TextOf(outputs), "interpreter state was reset");

        // The process was replaced, so a later cell still runs.
        var recovered = await session.ExecuteAsync("'alive'", new StubExecutionContext());
        Assert.AreEqual("'alive'", recovered[0].Content);
    }

    // --- startup budget ---

    /// <summary>
    /// Running Python in another process trades a warm in-process interpreter for a spawn, a
    /// handshake, and a bootstrap, and the whole design rests on that trade staying cheap. This is
    /// a tripwire rather than a benchmark: the ceiling is loose enough for a loaded shared runner,
    /// so it catches a startup path that has become pathological, not one that has slowed a little.
    /// The measurement is printed either way, which is what makes a gradual drift visible in the
    /// logs before it ever trips.
    /// <para>
    /// The first execute is included because <c>StartAsync</c> sends the bootstrap without waiting
    /// for it to be applied, so only a completed round trip proves the session is ready to run a
    /// cell. Interpreter discovery is stubbed here, as everywhere in this class, so what is being
    /// timed is the host and not the search that precedes it.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task Startup_ThroughTheFirstCell_StaysWithinBudget()
    {
        var budget = TimeSpan.FromSeconds(10);

        var stopwatch = Stopwatch.StartNew();
        await using var session = await StartedSessionAsync();
        var started = stopwatch.Elapsed;

        var outputs = await session.ExecuteAsync("1 + 1", new StubExecutionContext());
        var total = stopwatch.Elapsed;

        Assert.AreEqual("2", outputs[0].Content, "the first cell should have run");

        Console.WriteLine(
            $"Startup budget: spawn, handshake, and bootstrap {started.TotalMilliseconds:F0} ms, " +
            $"through the first cell {total.TotalMilliseconds:F0} ms, budget {budget.TotalSeconds:F0} s.");

        Assert.IsTrue(total < budget,
            $"Starting the host and running one cell took {total.TotalSeconds:F1} s, " +
            $"over the {budget.TotalSeconds:F0} s budget.");
    }

    // --- crash recovery ---

    [TestMethod]
    public async Task Crash_ReportsTheFailureAndRecoversOnTheNextCell()
    {
        await using var session = await StartedSessionAsync();

        var crashed = await session.ExecuteAsync("import os\nos._exit(9)", new StubExecutionContext());

        var error = crashed.Single(o => o.IsError);
        StringAssert.Contains(error.Content, "exited unexpectedly");

        var next = await session.ExecuteAsync("'recovered'", new StubExecutionContext());
        StringAssert.Contains(TextOf(next), "no longer available");
        Assert.AreEqual("'recovered'", next.Last().Content);
    }

    /// <summary>Reports each written output to a callback so arrival time can be measured.</summary>
    private sealed class StreamTimingContext : IExecutionContext
    {
        private readonly StubExecutionContext _inner = new();
        private readonly Action<CellOutput> _onWrite;

        public StreamTimingContext(Action<CellOutput> onWrite) => _onWrite = onWrite;

        public IVariableStore Variables => _inner.Variables;
        public CancellationToken CancellationToken => _inner.CancellationToken;
        public IThemeContext Theme => _inner.Theme;
        public LayoutCapabilities LayoutCapabilities => _inner.LayoutCapabilities;
        public IExtensionHostContext ExtensionHost => _inner.ExtensionHost;
        public INotebookMetadata NotebookMetadata => _inner.NotebookMetadata;
        public INotebookOperations Notebook => _inner.Notebook;
        public Guid CellId => _inner.CellId;
        public int ExecutionCount => _inner.ExecutionCount;

        public Task WriteOutputAsync(CellOutput output)
        {
            _onWrite(output);
            return _inner.WriteOutputAsync(output);
        }

        public Task DisplayAsync(CellOutput output) => _inner.DisplayAsync(output);
    }
}
