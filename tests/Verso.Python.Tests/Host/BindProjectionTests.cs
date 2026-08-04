using Verso.Abstractions;
using Verso.Python.Host;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.Tests.Interpreter;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Covers a trait in a real interpreter and the shared variable it is projected into, moving
/// each other. The halves are pinned down separately elsewhere; what this adds is that a value
/// crosses, arrives as the shape another kernel can use, and stops.
/// </summary>
/// <remarks>
/// The object under test is defined by the cells themselves rather than imported from a widget
/// library, so these run wherever there is a Python at all. What the projection asks of an object
/// is two methods, and writing them out in the cell is what makes that the thing being tested.
/// A widget is what an author will actually bind, and the one test that needs a real one says so.
/// </remarks>
[TestClass]
public sealed class BindProjectionTests
{
    private static string Python => PythonProbe.Require();

    /// <summary>
    /// How long a value is given to make the round trip and then stop moving. A round trip that
    /// has not happened by the end of it leaves the assertion after the wait to say so, so this
    /// bounds the test rather than deciding whether it passes.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    /// <summary>The smallest object a trait can be projected from: no widget library involved.</summary>
    private const string DialSource = @"
class Dial:
    def __init__(self, **values):
        self._values = dict(values)
        self._watchers = {}
        self.seen = []

    def has_trait(self, name):
        return name in self._values

    def trait_names(self):
        return list(self._values)

    def observe(self, handler, names=None):
        self._watchers.setdefault(names, []).append(handler)

    def unobserve(self, handler, names=None):
        watchers = self._watchers.get(names) or []
        if handler in watchers:
            watchers.remove(handler)

    def __getattr__(self, name):
        try:
            return self.__dict__[""_values""][name]
        except KeyError:
            raise AttributeError(name)

    def __setattr__(self, name, value):
        values = self.__dict__.get(""_values"")
        if values is None or name not in values:
            object.__setattr__(self, name, value)
            return

        old = values[name]
        values[name] = value
        if old == value:
            return

        self.seen.append(value)
        for handler in list(self._watchers.get(name) or []):
            handler({""name"": name, ""old"": old, ""new"": value})

";

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

    /// <summary>Run a cell and fail with its own output rather than with an empty assertion.</summary>
    private static async Task<string> RunAsync(
        PythonHostSession session, StubExecutionContext context, string code)
    {
        var outputs = await session.ExecuteAsync(code, context);
        var text = TextOf(outputs);

        Assert.IsFalse(outputs.Any(o => o.IsError), $"The cell failed: {text}");
        return text;
    }

    /// <summary>Wait until a condition holds, or give up and let the caller's assertion report it.</summary>
    private static async Task WaitUntilAsync(Func<bool> settled, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? Settle);
        while (DateTime.UtcNow < deadline && !settled())
            await Task.Delay(25);
    }

    // --- a trait reaching the other kernels ---

    [TestMethod]
    public async Task BindingSharesWhatTheTraitAlreadyHolds()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(value=3)");

        var outcome = await session.BindAsync(context, "dial", "value", "threshold", CancellationToken.None);

        Assert.IsTrue(outcome.Succeeded, outcome.Reason);
        Assert.AreEqual("threshold", outcome.Projection!.Name);

        // Readable straight away rather than only after the widget is next touched, which is what
        // lets the cell below a bind use the name.
        Assert.AreEqual(3L, context.Variables.Get<long>("threshold"));
    }

    [TestMethod]
    public async Task ChangingTheTraitChangesTheSharedVariable()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(value=3)");
        await session.BindAsync(context, "dial", "value", "threshold", CancellationToken.None);

        await RunAsync(session, context, "dial.value = 98");
        await WaitUntilAsync(() => context.Variables.Get<long>("threshold") == 98L);

        Assert.AreEqual(98L, context.Variables.Get<long>("threshold"));
    }

    [TestMethod]
    public async Task WritingTheSharedVariableChangesTheTrait()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(value=3)");
        await session.BindAsync(context, "dial", "value", "threshold", CancellationToken.None);

        // What a C# cell setting the name comes to. Nothing else about the notebook is involved.
        context.Variables.Set("threshold", 61L);
        await Task.Delay(Settle);

        Assert.AreEqual("61", (await RunAsync(session, context, "print(dial.value)")).Trim());
    }

    // --- the assertion the whole thing exists for ---

    [TestMethod]
    public async Task AValueWrittenFromAnotherKernelSettlesAfterOneRoundTrip()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(value=3)");
        await session.BindAsync(context, "dial", "value", "threshold", CancellationToken.None);

        var writes = 0;
        context.Variables.OnVariablesChanged += () => Interlocked.Increment(ref writes);

        context.Variables.Set("threshold", 61L);   // one write, ours
        await Task.Delay(Settle);

        Assert.AreEqual(1, Volatile.Read(ref writes),
            "The value the trait echoed back was written to the store again, which is where an "
            + "endless exchange starts.");

        // The trait was set once. Twice would mean the store wrote back what it had just been
        // told, and the pair would be trading the same value for as long as the session lived.
        var settled = await RunAsync(session, context, "print(dial.value, dial.seen)");
        Assert.AreEqual("61 [61]", settled.Trim());
        Assert.AreEqual(61L, context.Variables.Get<long>("threshold"));
    }

    [TestMethod]
    public async Task AValueChangedInPythonSettlesAfterOneRoundTrip()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(value=3)");
        await session.BindAsync(context, "dial", "value", "threshold", CancellationToken.None);

        await RunAsync(session, context, "dial.value = 98");
        await WaitUntilAsync(() => context.Variables.Get<long>("threshold") == 98L);

        var writes = 0;
        context.Variables.OnVariablesChanged += () => Interlocked.Increment(ref writes);
        await Task.Delay(Settle);

        Assert.AreEqual(0, Volatile.Read(ref writes),
            "The store kept changing after the trait had stopped.");

        // Set once, by the cell. A store write pushed back into the trait would add a second.
        var settled = await RunAsync(session, context, "print(dial.value, dial.seen)");
        Assert.AreEqual("98 [98]", settled.Trim());
    }

    [TestMethod]
    public async Task ACellPublishingItsScopeDoesNotUndoWhatTheTraitDidWhileItRan()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(value=3)");
        await session.BindAsync(context, "dial", "value", "threshold", CancellationToken.None);

        // The projected name is an ordinary shared variable, so it is placed in the cell's scope
        // like any other. The copy in the scope is a number and goes nowhere when the trait moves,
        // so publishing the scope at the end of a cell that changed the trait would put the value
        // back as it was. What stops it is the rule the publish already follows: a name something
        // other than the cell wrote while it ran is left alone.
        await RunAsync(session, context, "dial.value = 98");
        await WaitUntilAsync(() => context.Variables.Get<long>("threshold") == 98L);

        Assert.AreEqual(98L, context.Variables.Get<long>("threshold"));

        // And again on the cell after it, which is where the stale copy would still be sitting.
        await RunAsync(session, context, "pass");
        await Task.Delay(Settle);

        Assert.AreEqual(98L, context.Variables.Get<long>("threshold"));
        Assert.AreEqual("98", (await RunAsync(session, context, "print(dial.value)")).Trim());
    }

    // --- type fidelity ---

    [TestMethod]
    public async Task TheOrdinaryShapesCrossAsTheOrdinaryShapes()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + @"
import datetime, decimal, uuid
dial = Dial(
    count=0, ratio=0.0, flag=False, label="""", items=[], pairs={},
    blob=b"""", stamp=None, moment=None, price=None, key=None)
");

        foreach (var trait in new[]
                 { "count", "ratio", "flag", "label", "items", "pairs", "blob", "stamp", "moment", "price", "key" })
        {
            var outcome = await session.BindAsync(context, "dial", trait, trait, CancellationToken.None);
            Assert.IsTrue(outcome.Succeeded, $"{trait}: {outcome.Reason}");
        }

        await RunAsync(session, context, @"
import datetime, decimal, uuid
dial.count = 42
dial.ratio = 1.5
dial.flag = True
dial.label = ""ready""
dial.items = [1, 2, 3]
dial.pairs = {""a"": 1}
dial.blob = b""\x00\x01""
dial.stamp = datetime.date(2026, 8, 4)
dial.moment = datetime.datetime(2026, 8, 4, 12, 30)
dial.price = decimal.Decimal(""12.75"")
dial.key = uuid.UUID(""2f6c7b1e-0a4d-4f5e-8b3a-9c1d2e3f4a5b"")
");

        await WaitUntilAsync(() => context.Variables.TryGet<Guid>("key", out _));

        Assert.AreEqual(42L, context.Variables.Get<long>("count"));
        Assert.AreEqual(1.5d, context.Variables.Get<double>("ratio"));
        Assert.AreEqual(true, context.Variables.Get<bool>("flag"));
        Assert.AreEqual("ready", context.Variables.Get<string>("label"));
        CollectionAssert.AreEqual(
            new object?[] { 1L, 2L, 3L }, context.Variables.Get<List<object?>>("items"));
        Assert.AreEqual(1L, context.Variables.Get<Dictionary<string, object?>>("pairs")!["a"]);
        CollectionAssert.AreEqual(new byte[] { 0, 1 }, context.Variables.Get<byte[]>("blob"));
        Assert.AreEqual(new DateOnly(2026, 8, 4), context.Variables.Get<DateOnly>("stamp"));
        Assert.AreEqual(
            new DateTime(2026, 8, 4, 12, 30, 0), context.Variables.Get<DateTime>("moment"));
        Assert.AreEqual(12.75m, context.Variables.Get<decimal>("price"));
        Assert.AreEqual(
            Guid.Parse("2f6c7b1e-0a4d-4f5e-8b3a-9c1d2e3f4a5b"), context.Variables.Get<Guid>("key"));
    }

    [TestMethod]
    public async Task AValueWrittenAsAnotherKernelWritesItReachesTheTraitAsItself()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(when=None)");
        await session.BindAsync(context, "dial", "when", "when", CancellationToken.None);

        context.Variables.Set("when", new DateTime(2026, 8, 4, 9, 15, 0));
        await Task.Delay(Settle);

        // A date crosses tagged and is rebuilt rather than arriving as the text it prints as,
        // which is what lets the cell below do arithmetic with it.
        Assert.AreEqual(
            "datetime 2026-08-04 09:15:00",
            (await RunAsync(session, context, "print(type(dial.when).__mro__[1].__name__, dial.when)")).Trim());
    }

    // --- refusals ---

    [TestMethod]
    public async Task AnObjectThatIsNotInScopeIsRefusedWithTheReason()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, "x = 1");

        var outcome = await session.BindAsync(context, "missing", "value", null, CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.Reason, "could not be evaluated");
    }

    [TestMethod]
    public async Task AnObjectWithNoObservableTraitsIsRefused()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, "count = 7");

        var outcome = await session.BindAsync(context, "count", "value", null, CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.Reason, "no observable traits");
    }

    [TestMethod]
    public async Task AValueTooLargeToKeepSendingIsRefused()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context,
            DialSource + "dial = Dial(samples=[float(i) for i in range(200000)])");

        var outcome = await session.BindAsync(context, "dial", "samples", "samples", CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.Reason, "over the");
        Assert.IsFalse(context.Variables.TryGet<object>("samples", out _),
            "A refused projection wrote the variable anyway.");
    }

    [TestMethod]
    public async Task ATraitHoldingAWidgetIsRefused()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        var probe = await session.ExecuteAsync("import ipywidgets", context);
        if (probe.Any(o => o.IsError))
            Assert.Inconclusive("ipywidgets is not importable in this environment.");

        await RunAsync(session, context, "import ipywidgets; slider = ipywidgets.IntSlider(value=4)");

        // layout and style are the two an author is most likely to reach for, and both are
        // widgets rather than data.
        var outcome = await session.BindAsync(context, "slider", "layout", null, CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.Reason, "live object");

        // The trait beside it is data, and binding that is the thing to do instead.
        var value = await session.BindAsync(context, "slider", "value", "threshold", CancellationToken.None);
        Assert.IsTrue(value.Succeeded, value.Reason);
        Assert.AreEqual(4L, context.Variables.Get<long>("threshold"));
    }

    // --- the registry ---

    [TestMethod]
    public async Task TheSameTraitBoundAgainMovesRatherThanDoubling()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(value=3)");
        await session.BindAsync(context, "dial", "value", "first", CancellationToken.None);

        var outcome = await session.BindAsync(context, "dial", "value", "second", CancellationToken.None);

        Assert.IsTrue(outcome.Succeeded, outcome.Reason);
        Assert.AreEqual("first", outcome.Replaced);
        CollectionAssert.AreEqual(
            new[] { "second" }, session.Projections.Select(p => p.Name).ToArray());

        await RunAsync(session, context, "dial.value = 11");
        await WaitUntilAsync(() => context.Variables.Get<long>("second") == 11L);

        Assert.AreEqual(11L, context.Variables.Get<long>("second"));
        Assert.AreEqual(3L, context.Variables.Get<long>("first"),
            "The name that was replaced kept following the trait.");
    }

    [TestMethod]
    public async Task TwoTraitsOfOneObjectAreSeparateProjections()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(value=3, label='start')");
        await session.BindAsync(context, "dial", "value", "threshold", CancellationToken.None);
        await session.BindAsync(context, "dial", "label", "caption", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "caption", "threshold" }, session.Projections.Select(p => p.Name).ToArray());
    }

    [TestMethod]
    public async Task RemovingAProjectionStopsItAndLeavesTheValueBehind()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(value=3)");
        await session.BindAsync(context, "dial", "value", "threshold", CancellationToken.None);

        var removed = await session.UnbindAsync("threshold", CancellationToken.None);

        Assert.IsTrue(removed.Succeeded);
        Assert.AreEqual(0, session.Projections.Count);

        await RunAsync(session, context, "dial.value = 98");
        await Task.Delay(Settle);

        // Still an ordinary variable holding what it last had, because the cells reading it are
        // not written to cope with a name that disappears.
        Assert.AreEqual(3L, context.Variables.Get<long>("threshold"));
    }

    [TestMethod]
    public async Task RemovingANameThatIsNotProjectedSaysSo()
    {
        await using var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, "x = 1");

        var outcome = await session.UnbindAsync("threshold", CancellationToken.None);

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.Reason, "threshold");
    }

    [TestMethod]
    public async Task TheSessionEndingForgetsEveryProjection()
    {
        var session = await StartedSessionAsync();
        var context = new StubExecutionContext();

        await RunAsync(session, context, DialSource + "dial = Dial(value=3)");
        await session.BindAsync(context, "dial", "value", "threshold", CancellationToken.None);
        Assert.AreEqual(1, session.Projections.Count);

        await session.DisposeAsync();

        Assert.AreEqual(0, session.Projections.Count);

        // The store is no longer watched, so a write goes nowhere rather than to an interpreter
        // that has gone. Nothing to assert but that it does not raise.
        context.Variables.Set("threshold", 99L);
        Assert.AreEqual(99L, context.Variables.Get<long>("threshold"));
    }
}
