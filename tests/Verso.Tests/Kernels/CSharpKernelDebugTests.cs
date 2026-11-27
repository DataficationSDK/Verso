using Verso.Kernels;
using Verso.Testing.Fakes;
using Verso.Testing.Stubs;

namespace Verso.Tests.Kernels;

[TestClass]
[DoNotParallelize] // Console.Out is process-wide
public sealed class CSharpKernelDebugTests
{
    // The model function sits in a cell of its own so there is something to step into, and so a
    // breakpoint can sit in a cell other than the one that is running.
    private const string ModelCell = """
        double Model(int x)
        {
            var basis = offset + factor * x;
            var wave = 4 * Math.Sin(x / 3.0);
            return basis + wave;
        }
        """;

    private const string SeriesCell = """
        var xs = Enumerable.Range(1, 30).ToArray();
        var ys = xs.Select(x => Model(x)).ToArray();
        var max = ys.Max();
        Console.WriteLine($"max = {max:F3}");
        """;

    private CSharpKernel _kernel = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new CSharpKernel();
        await _kernel.InitializeAsync();
    }

    [TestCleanup]
    public async Task Cleanup() => await _kernel.DisposeAsync();

    [TestMethod]
    public async Task Breakpoint_StopsOnItsLine_WithTheVariablesInScope()
    {
        await _kernel.ExecuteAsync("var factor = 0.45;\nvar offset = 20.0;", new StubExecutionContext());

        var context = new StubExecutionContext();
        var session = new FakeDebugSession().SetBreakpoints(context.CellId, 3);

        var outputs = await _kernel.DebugAsync(
            "var xs = Enumerable.Range(1, 30).ToArray();\nvar scaled = xs.Select(x => offset + factor * x).ToArray();\nvar max = scaled.Max();\nmax",
            context, session);

        AssertNoError(outputs);
        Assert.AreEqual(1, session.Stops.Count);

        var stop = session.Stops[0];
        Assert.AreEqual(DebugStopReason.Breakpoint, stop.Reason);
        Assert.AreEqual(1, stop.Frames.Count);
        Assert.AreEqual("", stop.Frames[0].Name, "Code outside any function has no name.");
        Assert.AreEqual(context.CellId, stop.Frames[0].CellId);
        Assert.AreEqual(3, stop.Frames[0].Line);

        // What the cell declared above the line, then what it reads from an earlier cell. `max`
        // is declared on the line itself and has no value yet.
        CollectionAssert.AreEqual(
            new[] { "xs", "scaled", "offset", "factor" },
            stop.Frames[0].Variables.Select(v => v.Name).ToArray());
        Assert.AreEqual("0.45", Variable(stop, "factor").Value);
        Assert.AreEqual("int[]", Variable(stop, "xs").TypeName);
    }

    [TestMethod]
    public async Task Stepping_GoesIntoOverAndOutOfAFunctionTheCellCalls()
    {
        await _kernel.ExecuteAsync("var factor = 0.45;\nvar offset = 20.0;", new StubExecutionContext());

        var modelContext = new StubExecutionContext();
        AssertNoError(await _kernel.DebugAsync(ModelCell, modelContext, new FakeDebugSession()));

        var context = new StubExecutionContext();
        var session = new FakeDebugSession(
                DebugResumeAction.StepInto,
                DebugResumeAction.StepOver,
                DebugResumeAction.StepOver,
                DebugResumeAction.StepOut,
                DebugResumeAction.StepOver,
                DebugResumeAction.Continue)
            .SetBreakpoints(context.CellId, 2);
        session.OnStop = _ => session.SetBreakpoints(context.CellId); // one stop from the breakpoint, the rest from steps

        AssertNoError(await _kernel.DebugAsync(SeriesCell, context, session));

        var where = session.Stops.Select(s => (s.Frames[0].CellId, s.Frames[0].Line, s.Frames[0].Name)).ToArray();
        CollectionAssert.AreEqual(
            new (Guid?, int, string)[]
            {
                (context.CellId, 2, ""),            // the breakpoint
                (modelContext.CellId, 3, "Model"),  // into the function, in the cell that defined it
                (modelContext.CellId, 4, "Model"),
                (modelContext.CellId, 5, "Model"),
                (context.CellId, 3, ""),            // out, past the other twenty-nine calls
                (context.CellId, 4, ""),
            },
            where);

        Assert.AreEqual(DebugStopReason.Breakpoint, session.Stops[0].Reason);
        Assert.IsTrue(session.Stops.Skip(1).All(s => s.Reason == DebugStopReason.Step));

        // Inside the function: its parameter, its locals so far, and the notebook variables it reads.
        var inFunction = session.Stops[2];
        CollectionAssert.AreEqual(
            new[] { "x", "basis", "offset", "factor" },
            inFunction.Frames[0].Variables.Select(v => v.Name).ToArray());
        Assert.AreEqual("1", Variable(inFunction, "x").Value);
        Assert.AreEqual("20.45", Variable(inFunction, "basis").Value);

        // And the call that led there: the cell's own line, which made the call.
        Assert.AreEqual(2, inFunction.Frames.Count);
        Assert.AreEqual(context.CellId, inFunction.Frames[1].CellId);
        Assert.AreEqual(2, inFunction.Frames[1].Line);
        Assert.AreEqual("", inFunction.Frames[1].Name);
    }

    [TestMethod]
    public async Task ABreakpointInAnEarlierCellsFunction_StopsACellThatCallsIt()
    {
        await _kernel.ExecuteAsync("var factor = 0.45;\nvar offset = 20.0;", new StubExecutionContext());

        var modelContext = new StubExecutionContext();
        await _kernel.DebugAsync(ModelCell, modelContext, new FakeDebugSession());

        var context = new StubExecutionContext();
        var session = new FakeDebugSession().SetBreakpoints(modelContext.CellId, 4);
        session.OnStop = _ => session.SetBreakpoints(modelContext.CellId);

        AssertNoError(await _kernel.DebugAsync("Model(7)", context, session));

        Assert.AreEqual(1, session.Stops.Count);
        Assert.AreEqual(modelContext.CellId, session.Stops[0].Frames[0].CellId);
        Assert.AreEqual(4, session.Stops[0].Frames[0].Line);
        Assert.AreEqual("7", Variable(session.Stops[0], "x").Value);
    }

    [TestMethod]
    public async Task ADebuggedCell_LeavesTheSameStateBehindAsAnOrdinaryRun()
    {
        var session = new FakeDebugSession(DebugResumeAction.StepOver, DebugResumeAction.StepOver);
        var context = new StubExecutionContext();
        session.SetBreakpoints(context.CellId, 1);

        AssertNoError(await _kernel.DebugAsync("var a = 2;\nvar b = a * 21;\nint Twice(int n) => n * 2;", context, session));

        // The next cell runs the ordinary way and sees what the debugged one declared, and nothing
        // the debugger added.
        var next = new StubExecutionContext();
        var outputs = await _kernel.ExecuteAsync("Twice(b)", next);
        AssertNoError(outputs);
        Assert.AreEqual("84", outputs[^1].Content);

        Assert.IsTrue(next.Variables.TryGet<int>("b", out var published));
        Assert.AreEqual(42, published);
        Assert.IsFalse(next.Variables.GetAll().Any(v => v.Name.StartsWith("__", StringComparison.Ordinal)),
            "Nothing the debugger added should be published as a notebook variable.");
    }

    [TestMethod]
    public async Task WithDebuggingOffAgain_AFunctionCompiledForDebugging_RunsStraightThrough()
    {
        var modelContext = new StubExecutionContext();
        var first = new FakeDebugSession().SetBreakpoints(modelContext.CellId, 3);
        await _kernel.DebugAsync("int Add(int a, int b)\n{\n    var sum = a + b;\n    return sum;\n}\nAdd(1, 2)", modelContext, first);
        Assert.AreEqual(1, first.Stops.Count);

        var outputs = await _kernel.ExecuteAsync("Add(20, 22)", new StubExecutionContext());

        AssertNoError(outputs);
        Assert.AreEqual("42", outputs[^1].Content);
        Assert.AreEqual(1, first.Stops.Count, "The session of a run that has ended must not hear from later runs.");
    }

    [TestMethod]
    public async Task SingleSteppingAMixedCell_ProducesTheOutputOfAnOrdinaryRun()
    {
        const string cell = """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            record Point(double X, double Y);

            class Scaler
            {
                private readonly double _by;
                public Scaler(double by) { _by = by; }
                public double Apply(double v)
                {
                    var r = v * _by;
                    return r;
                }
                public IEnumerable<int> Squares(int n)
                {
                    for (var i = 0; i < n; i++)
                    {
                        yield return i * i;
                    }
                }
            }

            async Task<int> LoadAsync(int n)
            {
                await Task.Delay(1);
                var doubled = n * 2;
                await Task.Yield();
                return doubled;
            }

            T First<T>(IEnumerable<T> source) where T : struct
            {
                foreach (var e in source)
                {
                    return e;
                }
                return default;
            }

            int WithSpan()
            {
                Span<int> buffer = stackalloc int[3];
                buffer[0] = 7;
                var first = buffer[0];
                return first;
            }

            var scaler = new Scaler(0.45);
            var (a, b) = (1, 2);
            var total = 0.0;
            foreach (var q in scaler.Squares(4))
            {
                if (q % 2 == 0) total += scaler.Apply(q);
                else
                    total -= 1;
            }
            object o = WithSpan();
            if (o is int number && number > 3)
            {
                total += number;
            }
            var loaded = await LoadAsync(a + b);
            Func<int, int> square = v =>
            {
                var squared = v * v;
                return squared;
            };
            switch (loaded)
            {
                case 6:
                    total += square(3);
                    break;
                default:
                    total = -1;
                    break;
            }
            try
            {
                var points = new List<Point> { new(1, 2), new(3, 4) };
                total += points.Sum(p => p.X);
                throw new InvalidOperationException("x");
            }
            catch (InvalidOperationException ex)
            {
                total += ex.Message.Length;
            }
            Console.WriteLine($"total = {total}, first = {First(new[] { 5, 6 })}");
            """;

        var plain = new CSharpKernel();
        await plain.InitializeAsync();
        var expected = await plain.ExecuteAsync(cell, new StubExecutionContext());
        await plain.DisposeAsync();
        AssertNoError(expected);

        var context = new StubExecutionContext();
        var session = new FakeDebugSession(Enumerable.Repeat(DebugResumeAction.StepInto, 500).ToArray());
        session.SetBreakpoints(context.CellId, 1);

        var outputs = await _kernel.DebugAsync(cell, context, session);

        AssertNoError(outputs);
        Assert.AreEqual(expected[^1].Content, outputs[^1].Content);

        var lines = session.Stops.Select(s => s.Frames[0].Line).ToHashSet();
        foreach (var expectedLine in new[] { 9, 12, 19, 26, 29, 36, 43, 54, 56, 61, 66, 72, 73, 80, 82, 86, 88 })
            Assert.IsTrue(lines.Contains(expectedLine), $"No stop on line {expectedLine}. Stopped on: {string.Join(", ", lines.OrderBy(l => l))}");

        // Inside a method of a type the cell declares, `this` is what there is to show.
        var inMethod = session.Stops.First(s => s.Frames[0].Name == "Scaler.Apply");
        CollectionAssert.Contains(inMethod.Frames[0].Variables.Select(v => v.Name).ToArray(), "this");

        // A value that cannot be boxed is left out rather than breaking the cell.
        var withSpan = session.Stops.Last(s => s.Frames[0].Name == "WithSpan");
        CollectionAssert.DoesNotContain(withSpan.Frames[0].Variables.Select(v => v.Name).ToArray(), "buffer");
        CollectionAssert.Contains(withSpan.Frames[0].Variables.Select(v => v.Name).ToArray(), "first");

        // A step into an awaited function still knows who called it once the await has run.
        var afterAwait = session.Stops.Last(s => s.Frames[0].Name == "LoadAsync");
        Assert.AreEqual(29, afterAwait.Frames[0].Line);
        Assert.AreEqual(2, afterAwait.Frames.Count);
    }

    [TestMethod]
    public async Task ABreakpointOnALineWithNoStatement_TakesTheNextStatementBelow()
    {
        var context = new StubExecutionContext();
        var session = new FakeDebugSession().SetBreakpoints(context.CellId, 2);

        await _kernel.DebugAsync("var a = 1;\n\n// nothing to stop at here\nvar b = a + 1;\nb", context, session);

        Assert.AreEqual(1, session.Stops.Count);
        Assert.AreEqual(4, session.Stops[0].Frames[0].Line);
    }

    [TestMethod]
    public async Task ABreakpointInsideAStatementSpanningLines_TakesThatStatement()
    {
        var context = new StubExecutionContext();
        var session = new FakeDebugSession().SetBreakpoints(context.CellId, 3);

        await _kernel.DebugAsync("var a = 1;\nvar b = Enumerable.Range(0, a)\n    .Select(n => n + 1)\n    .ToArray();\nb.Length", context, session);

        Assert.AreEqual(1, session.Stops.Count);
        Assert.AreEqual(2, session.Stops[0].Frames[0].Line);
    }

    [TestMethod]
    public async Task CancellingAStoppedCell_EndsIt()
    {
        using var cts = new CancellationTokenSource();
        var context = new StubExecutionContext { CancellationToken = cts.Token };
        var session = new FakeDebugSession().SetBreakpoints(context.CellId, 2);
        session.OnStop = _ => cts.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => _kernel.DebugAsync("var a = 1;\nvar b = 2;\nConsole.WriteLine(\"ran on\");", context, session));

        // The kernel is still usable, and the cancelled cell did not get past its stop.
        var outputs = await _kernel.ExecuteAsync("1 + 1", new StubExecutionContext());
        Assert.AreEqual("2", outputs[^1].Content);
    }

    [TestMethod]
    public async Task ACellThatDoesNotCompile_ReportsTheSameErrorAsAnOrdinaryRun()
    {
        const string broken = "var a = 1;\nvar b = a +;\n";

        var expected = await _kernel.ExecuteAsync(broken, new StubExecutionContext());

        var context = new StubExecutionContext();
        var session = new FakeDebugSession().SetBreakpoints(context.CellId, 1);
        var outputs = await _kernel.DebugAsync(broken, context, session);

        Assert.AreEqual(0, session.Stops.Count);
        Assert.IsTrue(outputs[0].IsError);
        Assert.AreEqual(expected[0].Content, outputs[0].Content);
        Assert.AreEqual(0, context.WrittenOutputs.Count, "A cell that fails to compile needs no note about debugging.");
    }

    [TestMethod]
    public async Task AddingABreakpointWhileStopped_TakesEffectInTheSameRun()
    {
        var context = new StubExecutionContext();
        var session = new FakeDebugSession().SetBreakpoints(context.CellId, 1);
        session.OnStop = stop =>
        {
            if (stop.Frames[0].Line == 1)
                session.SetBreakpoints(context.CellId, 3);
        };

        await _kernel.DebugAsync("var a = 1;\nvar b = 2;\nvar c = a + b;\nc", context, session);

        CollectionAssert.AreEqual(new[] { 1, 3 }, session.Stops.Select(s => s.Frames[0].Line).ToArray());
    }

    [TestMethod]
    public async Task PackageDirectives_DoNotMoveTheLinesBelowThem()
    {
        // The ordinary run strips a directive's line. A debugged run has to keep line numbers.
        var context = new StubExecutionContext();
        var session = new FakeDebugSession().SetBreakpoints(context.CellId, 3);

        var outputs = await _kernel.DebugAsync(
            "#i \"nuget: https://api.nuget.org/v3/index.json\"\nvar a = 1;\nvar b = a + 1;\nb", context, session);

        AssertNoError(outputs);
        Assert.AreEqual(1, session.Stops.Count);
        Assert.AreEqual(3, session.Stops[0].Frames[0].Line);
        CollectionAssert.Contains(session.Stops[0].Frames[0].Variables.Select(v => v.Name).ToArray(), "a");
    }

    [TestMethod]
    public async Task ShowingASequenceThatHasNotRunYet_DoesNotRunIt()
    {
        // The sequence can only be read once. A debugger that counted it to show a value would
        // leave the cell with nothing to read.
        const string cell = """
            IEnumerable<int> Once()
            {
                yield return 1;
                yield return 2;
            }
            var pending = Once();
            var filtered = pending.Where(n => n > 0);
            var total = filtered.Sum();
            total
            """;

        var context = new StubExecutionContext();
        var session = new FakeDebugSession().SetBreakpoints(context.CellId, 8);

        var outputs = await _kernel.DebugAsync(cell, context, session);

        AssertNoError(outputs);
        Assert.AreEqual("3", outputs[^1].Content);
        Assert.AreEqual(1, session.Stops.Count, "Showing the value must not run the cell's own code and stop in it.");
        StringAssert.StartsWith(Variable(session.Stops[0], "pending").Value, "{");
        StringAssert.StartsWith(Variable(session.Stops[0], "filtered").Value, "{");
    }

    [TestMethod]
    public async Task ShowingAValue_DoesNotStopInsideTheCodeThatFormatsIt()
    {
        const string cell = """
            class Money
            {
                public decimal Amount;
                public override string ToString()
                {
                    var text = Amount.ToString("0.00");
                    return text + " EUR";
                }
            }
            var price = new Money { Amount = 12.5m };
            var copy = price;
            copy.Amount
            """;

        var context = new StubExecutionContext();
        var session = new FakeDebugSession().SetBreakpoints(context.CellId, 6, 11);

        AssertNoError(await _kernel.DebugAsync(cell, context, session));

        Assert.AreEqual(1, session.Stops.Count);
        Assert.AreEqual(11, session.Stops[0].Frames[0].Line);
        Assert.AreEqual("12.50 EUR", Variable(session.Stops[0], "price").Value);
    }

    [TestMethod]
    public void Values_AreDescribedBriefly()
    {
        Assert.AreEqual("null", Verso.Kernels.Debugging.CSharpDebugController.DescribeValue(null));
        Assert.AreEqual("\"hi\"", Verso.Kernels.Debugging.CSharpDebugController.DescribeValue("hi"));
        Assert.AreEqual("true", Verso.Kernels.Debugging.CSharpDebugController.DescribeValue(true));
        Assert.AreEqual("Count = 3", Verso.Kernels.Debugging.CSharpDebugController.DescribeValue(new[] { 1, 2, 3 }));
        Assert.AreEqual(200, Verso.Kernels.Debugging.CSharpDebugController.DescribeValue(new string('x', 500)).Length);
    }

    private static DebugVariable Variable(DebugStop stop, string name)
        => stop.Frames[0].Variables.Single(v => v.Name == name);

    private static void AssertNoError(IReadOnlyList<CellOutput> outputs)
        => Assert.IsFalse(outputs.Any(o => o.IsError), outputs.FirstOrDefault(o => o.IsError)?.Content);
}
