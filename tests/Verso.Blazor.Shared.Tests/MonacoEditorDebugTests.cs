using System.Text.Json;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// How the editor hands breakpoints and the stopped line to the script side. Every code cell's
/// editor is re-rendered whenever any cell's breakpoints change, so the guard against repeating
/// an unchanged state matters as much as the state itself.
/// </summary>
[TestClass]
public sealed class MonacoEditorDebugTests : BunitTestContext
{
    private IRenderedComponent<MonacoEditor> Render(
        bool gutter = false,
        IReadOnlyList<int>? breakpoints = null,
        int? stoppedLine = null,
        IReadOnlyList<int>? callerLines = null,
        Action<IReadOnlyList<int>>? onBreakpointsChanged = null)
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        return TestContext.RenderComponent<MonacoEditor>(p =>
        {
            p.Add(e => e.Value, "var x = 1;\nvar y = 2;\nvar z = 3;")
             .Add(e => e.Language, "csharp")
             .Add(e => e.DebugGutter, gutter)
             .Add(e => e.Breakpoints, breakpoints ?? Array.Empty<int>())
             .Add(e => e.StoppedLine, stoppedLine)
             .Add(e => e.CallerLines, callerLines ?? Array.Empty<int>());
            if (onBreakpointsChanged is not null)
                p.Add(e => e.OnBreakpointsChanged, onBreakpointsChanged);
        });
    }

    private IReadOnlyList<JSRuntimeInvocation> SetDebugStateCalls()
        => TestContext!.JSInterop.Invocations.Where(i => i.Identifier == "versoMonaco.setDebugState").ToList();

    // The options are an anonymous object, so they are read back the way the script side sees them.
    private static JsonElement AsJson(object? value)
        => JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement;

    private static int[] Ints(JsonElement array)
        => array.EnumerateArray().Select(e => e.GetInt32()).ToArray();

    [TestMethod]
    public void Create_CarriesTheDebugState()
    {
        Render(gutter: true, breakpoints: new[] { 1, 3 }, stoppedLine: 3, callerLines: new[] { 2 });

        var create = TestContext!.JSInterop.Invocations.Single(i => i.Identifier == "versoMonaco.create");
        var debug = AsJson(create.Arguments[1]).GetProperty("debug");

        Assert.IsTrue(debug.GetProperty("enabled").GetBoolean());
        CollectionAssert.AreEqual(new[] { 1, 3 }, Ints(debug.GetProperty("breakpoints")));
        Assert.AreEqual(3, debug.GetProperty("stoppedLine").GetInt32());
        CollectionAssert.AreEqual(new[] { 2 }, Ints(debug.GetProperty("callerLines")));
    }

    [TestMethod]
    public void Create_WithoutAStop_SendsZeroForTheStoppedLine()
    {
        // The script side treats 0 as "no line" rather than having to tell null apart.
        Render();

        var create = TestContext!.JSInterop.Invocations.Single(i => i.Identifier == "versoMonaco.create");
        var debug = AsJson(create.Arguments[1]).GetProperty("debug");

        Assert.IsFalse(debug.GetProperty("enabled").GetBoolean());
        Assert.AreEqual(0, debug.GetProperty("stoppedLine").GetInt32());
    }

    [TestMethod]
    public void Create_DoesNotAlsoSendTheStateSeparately()
    {
        Render(gutter: true, breakpoints: new[] { 2 });

        Assert.AreEqual(0, SetDebugStateCalls().Count);
    }

    [TestMethod]
    public void TurningTheGutterOn_SendsTheStateOnce()
    {
        var cut = Render();

        cut.SetParametersAndRender(p => p.Add(e => e.DebugGutter, true));

        var call = SetDebugStateCalls().Single();
        Assert.AreEqual(cut.Find("div").Id, call.Arguments[0]);
        Assert.IsTrue(AsJson(call.Arguments[1]).GetProperty("enabled").GetBoolean());
    }

    [TestMethod]
    public void ChangingBreakpoints_SendsTheNewLines()
    {
        var cut = Render(gutter: true, breakpoints: new[] { 1 });

        cut.SetParametersAndRender(p => p.Add(e => e.Breakpoints, new[] { 1, 3 }));

        var call = SetDebugStateCalls().Single();
        CollectionAssert.AreEqual(new[] { 1, 3 }, Ints(AsJson(call.Arguments[1]).GetProperty("breakpoints")));
    }

    [TestMethod]
    public void ChangingTheStoppedLine_SendsIt()
    {
        var cut = Render(gutter: true);

        cut.SetParametersAndRender(p => p.Add(e => e.StoppedLine, 2));

        var call = SetDebugStateCalls().Single();
        Assert.AreEqual(2, AsJson(call.Arguments[1]).GetProperty("stoppedLine").GetInt32());
    }

    [TestMethod]
    public void EqualLinesInANewList_SendNothing()
    {
        // The cell rebuilds its lists from the notebook's state each time, so the same lines
        // arrive in a new list on most renders and must not be mistaken for a change.
        var cut = Render(gutter: true, breakpoints: new[] { 1, 3 }, stoppedLine: 3, callerLines: new[] { 2 });

        cut.SetParametersAndRender(p => p
            .Add(e => e.Breakpoints, new List<int> { 1, 3 })
            .Add(e => e.CallerLines, new List<int> { 2 }));
        cut.SetParametersAndRender(p => p
            .Add(e => e.Breakpoints, new[] { 1, 3 })
            .Add(e => e.StoppedLine, 3));

        Assert.AreEqual(0, SetDebugStateCalls().Count);
    }

    [TestMethod]
    public void RepeatedChange_IsSentOnlyOnce()
    {
        var cut = Render(gutter: true);

        cut.SetParametersAndRender(p => p.Add(e => e.Breakpoints, new[] { 2 }));
        cut.SetParametersAndRender(p => p.Add(e => e.Breakpoints, new[] { 2 }));

        Assert.AreEqual(1, SetDebugStateCalls().Count);
    }

    [TestMethod]
    public void GutterOff_IgnoresLineChanges()
    {
        // While the gutter is off none of the lines is drawn, so a change to them has nothing
        // to update.
        var cut = Render();

        cut.SetParametersAndRender(p => p.Add(e => e.Breakpoints, new[] { 2 }));

        Assert.AreEqual(0, SetDebugStateCalls().Count);
    }

    [TestMethod]
    public void TurningTheGutterOff_SendsTheStateOnce()
    {
        var cut = Render(gutter: true, breakpoints: new[] { 2 });

        cut.SetParametersAndRender(p => p.Add(e => e.DebugGutter, false));

        var call = SetDebugStateCalls().Single();
        Assert.IsFalse(AsJson(call.Arguments[1]).GetProperty("enabled").GetBoolean());
    }

    [TestMethod]
    public async Task BreakpointsEdited_RaisesTheCallbackWithTheLines()
    {
        IReadOnlyList<int>? received = null;
        var cut = Render(gutter: true, onBreakpointsChanged: lines => received = lines);

        await cut.InvokeAsync(() => cut.Instance.OnBreakpointsEdited(new[] { 4, 7 }));

        Assert.IsNotNull(received);
        CollectionAssert.AreEqual(new[] { 4, 7 }, received!.ToArray());
    }
}
