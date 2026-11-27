using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Verso.Blazor.Shared.Resources;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The panel that turns debugging on and drives a stopped cell. It owns no state of its own
/// beyond which frame is selected, so the tests set the service's snapshot and check what the
/// panel shows for it and what it asks the service to do.
/// </summary>
[TestClass]
public sealed class DebugPanelTests : BunitTestContext
{
    private FakeNotebookService _service = default!;
    private CellModel _first = default!;
    private CellModel _second = default!;
    private CellModel _third = default!;
    private readonly List<Guid> _navigated = new();

    [TestInitialize]
    public void SetUp()
    {
        _first = new CellModel { Id = Guid.NewGuid(), Type = "code", Language = "csharp", Source = "int Add(int a, int b)\n{\n    return a + b;\n}" };
        _second = new CellModel { Id = Guid.NewGuid(), Type = "code", Language = "csharp", Source = "var x = 1;\nvar y = Add(x, 2);" };
        _third = new CellModel { Id = Guid.NewGuid(), Type = "code", Language = "csharp", Source = "Console.WriteLine(y);" };

        _service = new FakeNotebookService
        {
            Cells = new List<CellModel> { _first, _second, _third },
            RegisteredLanguages = new List<KernelLanguageInfo>
            {
                new("csharp", "C#", SupportsDebugging: true),
                new("fsharp", "F#"),
                new("python", "Python", SupportsDebugging: true),
            },
        };
    }

    private IRenderedComponent<DebugPanel> Render()
        => TestContext!.RenderComponent<DebugPanel>(parameters => parameters
            .Add(p => p.Service, _service)
            .Add(p => p.OnNavigate, EventCallback.Factory.Create<Guid>(this, id => _navigated.Add(id))));

    private static DebugSnapshot Enabled(DebugStop? stop = null, Dictionary<Guid, IReadOnlyList<int>>? breakpoints = null)
        => new(true, breakpoints ?? new Dictionary<Guid, IReadOnlyList<int>>(), stop);

    // Stopped inside Add, which the first cell defined and the second cell called.
    private DebugStop StopInAdd(DebugStopReason reason = DebugStopReason.Breakpoint)
        => new(reason, new[]
        {
            new DebugFrame("Add", _first.Id, 3, new[]
            {
                new DebugVariable("a", "int", "1"),
                new DebugVariable("b", "int", "2"),
            }),
            new DebugFrame("", _second.Id, 2, new[]
            {
                new DebugVariable("x", "int", "1"),
            }),
        });

    private static IElement ButtonWithText(IRenderedFragment cut, string text)
        => cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    private static IElement Control(IRenderedFragment cut, string label)
        => cut.Find($".verso-debug-control[aria-label='{label}']");

    private static IReadOnlyList<IElement> FrameRows(IRenderedFragment cut)
        => cut.FindAll(".verso-panel-row:not(.verso-debug-breakpoint-row)");

    private static string[] VariableNames(IRenderedFragment cut)
        => cut.FindAll("tr.verso-debug-variable").Select(r => r.Children[0].TextContent).ToArray();

    // ── Off ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Off_ExplainsTheModeAndOffersToTurnItOn()
    {
        var cut = Render();

        Assert.AreEqual(UI.Debug_Off_Title, cut.Find(".verso-panel-section-title").TextContent);
        StringAssert.Contains(cut.Markup, UI.Debug_Off_Body);
        Assert.IsNotNull(ButtonWithText(cut, UI.Debug_TurnOn));
        Assert.AreEqual(0, cut.FindAll(".verso-debug-control").Count,
            "The controls have nothing to drive until debugging is on.");
    }

    [TestMethod]
    public void Off_TurnOn_AsksTheServiceToEnable()
    {
        var cut = Render();

        ButtonWithText(cut, UI.Debug_TurnOn).Click();

        CollectionAssert.AreEqual(new[] { true }, _service.SetDebuggingCalls);
    }

    [TestMethod]
    public void Off_LanguagesLine_NamesOnlyDebuggableLanguages()
    {
        var cut = Render();

        var line = cut.Find(".verso-debug-text.verso-panel-secondary").TextContent;
        Assert.AreEqual(string.Format(UI.Debug_Off_Languages, "C#, Python"), line);
    }

    [TestMethod]
    public void Off_NoDebuggableLanguage_OmitsTheLanguagesLine()
    {
        _service.RegisteredLanguages = new List<KernelLanguageInfo> { new("fsharp", "F#") };

        var cut = Render();

        Assert.AreEqual(0, cut.FindAll(".verso-debug-text.verso-panel-secondary").Count);
    }

    // ── On, waiting ────────────────────────────────────────────────────

    [TestMethod]
    public void Waiting_ShowsStatusHintAndDisabledControls()
    {
        _service.DebugState = Enabled();

        var cut = Render();

        Assert.AreEqual(UI.Debug_Status_Waiting, cut.Find(".verso-debug-status .verso-panel-chip").TextContent);
        StringAssert.Contains(cut.Markup, UI.Debug_Hint_SetBreakpoint);

        var controls = cut.FindAll(".verso-debug-control");
        Assert.AreEqual(5, controls.Count);
        foreach (var control in controls)
        {
            Assert.IsTrue(control.HasAttribute("disabled"),
                $"'{control.GetAttribute("aria-label")}' should wait for a stop.");
        }

        Assert.IsFalse(cut.Markup.Contains(UI.Debug_CallStack),
            "There is no call stack until something stops.");
    }

    [TestMethod]
    public void Waiting_TurnOff_AsksTheServiceToDisable()
    {
        _service.DebugState = Enabled();
        var cut = Render();

        ButtonWithText(cut, UI.Debug_TurnOff).Click();

        CollectionAssert.AreEqual(new[] { false }, _service.SetDebuggingCalls);
    }

    // ── Stopped ────────────────────────────────────────────────────────

    [TestMethod]
    public void Stopped_AtBreakpoint_SaysSoAndWhere()
    {
        _service.DebugState = Enabled(StopInAdd());

        var cut = Render();

        Assert.AreEqual(UI.Debug_Status_AtBreakpoint, cut.Find(".verso-debug-status .verso-panel-chip").TextContent);
        // The location counts cells from 1 in the order the notebook shows them.
        Assert.AreEqual(string.Format(UI.Debug_Location_CellLine, 1, 3), cut.Find(".verso-debug-status-where").TextContent);
        Assert.IsFalse(cut.Markup.Contains(UI.Debug_Hint_SetBreakpoint));
    }

    [TestMethod]
    public void Stopped_AfterStep_SaysSo()
    {
        _service.DebugState = Enabled(StopInAdd(DebugStopReason.Step));

        var cut = Render();

        Assert.AreEqual(UI.Debug_Status_AfterStep, cut.Find(".verso-debug-status .verso-panel-chip").TextContent);
    }

    [TestMethod]
    public void Stopped_LocationUsesTheStoppedCellsPosition()
    {
        var stop = new DebugStop(DebugStopReason.Breakpoint, new[]
        {
            new DebugFrame("", _third.Id, 1, Array.Empty<DebugVariable>()),
        });
        _service.DebugState = Enabled(stop);

        var cut = Render();

        Assert.AreEqual(string.Format(UI.Debug_Location_CellLine, 3, 1), cut.Find(".verso-debug-status-where").TextContent);
    }

    [TestMethod]
    public void Stopped_OutsideAnyCell_NamesTheFunctionOrSaysItIsCode()
    {
        _service.DebugState = Enabled(new DebugStop(DebugStopReason.Step, new[]
        {
            new DebugFrame("helper", null, 4, Array.Empty<DebugVariable>()),
        }));
        Assert.AreEqual("helper", Render().Find(".verso-debug-status-where").TextContent);

        // With neither a cell nor a name there is still something to read beside the status.
        _service.DebugState = Enabled(new DebugStop(DebugStopReason.Step, new[]
        {
            new DebugFrame("", null, 4, Array.Empty<DebugVariable>()),
        }));
        Assert.AreEqual(UI.Debug_Frame_Unnamed, Render().Find(".verso-debug-status-where").TextContent);
    }

    [TestMethod]
    public void Stopped_ControlsAreEnabled()
    {
        _service.DebugState = Enabled(StopInAdd());

        var cut = Render();

        foreach (var control in cut.FindAll(".verso-debug-control"))
            Assert.IsFalse(control.HasAttribute("disabled"), $"'{control.GetAttribute("aria-label")}' is disabled.");
    }

    [TestMethod]
    public void Stopped_CallStack_ListsFramesInnermostFirst()
    {
        _service.DebugState = Enabled(StopInAdd());

        var cut = Render();

        var rows = FrameRows(cut);
        Assert.AreEqual(2, rows.Count);

        // A named frame shows its function and the cell it lives in.
        Assert.AreEqual("Add", rows[0].QuerySelector(".verso-debug-frame-name")!.TextContent);
        Assert.AreEqual(string.Format(UI.Debug_Location_CellLine, 1, 3),
            rows[0].QuerySelector(".verso-panel-secondary")!.TextContent);

        // Code outside any function is the cell itself, so the name already says which cell.
        Assert.AreEqual(string.Format(UI.Debug_Cell, 2), rows[1].QuerySelector(".verso-debug-frame-name")!.TextContent);
        Assert.AreEqual(string.Format(UI.Debug_Line, 2), rows[1].QuerySelector(".verso-panel-secondary")!.TextContent);

        Assert.IsTrue(rows[0].ClassList.Contains("verso-panel-row--active"));
        Assert.IsFalse(rows[1].ClassList.Contains("verso-panel-row--active"));
    }

    [TestMethod]
    public void Stopped_ShowsTheInnermostFramesVariables()
    {
        _service.DebugState = Enabled(StopInAdd());

        var cut = Render();

        CollectionAssert.AreEqual(new[] { "a", "b" }, VariableNames(cut));
        var first = cut.Find("tr.verso-debug-variable");
        Assert.AreEqual("int", first.Children[1].TextContent);
        Assert.AreEqual("1", first.Children[2].TextContent);
    }

    [TestMethod]
    public void Stopped_FrameWithNoVariables_SaysNothingIsInScope()
    {
        var stop = new DebugStop(DebugStopReason.Step, new[]
        {
            new DebugFrame("", _second.Id, 1, Array.Empty<DebugVariable>()),
        });
        _service.DebugState = Enabled(stop);

        var cut = Render();

        Assert.AreEqual(0, cut.FindAll("tr.verso-debug-variable").Count);
        StringAssert.Contains(cut.Markup, UI.Debug_Variables_Empty);
    }

    [TestMethod]
    public void SelectingAnotherFrame_ShowsItsVariablesAndNavigatesToItsCell()
    {
        _service.DebugState = Enabled(StopInAdd());
        var cut = Render();

        FrameRows(cut)[1].Click();

        CollectionAssert.AreEqual(new[] { "x" }, VariableNames(cut));
        CollectionAssert.AreEqual(new[] { _second.Id }, _navigated);
        Assert.IsTrue(FrameRows(cut)[1].ClassList.Contains("verso-panel-row--active"));
        Assert.IsFalse(FrameRows(cut)[0].ClassList.Contains("verso-panel-row--active"));
    }

    [TestMethod]
    public void NewStop_ReturnsTheSelectionToTheInnermostFrame()
    {
        // The reader's attention is wherever the cell has just stopped, not on the frame they
        // happened to be looking at before the last step.
        _service.DebugState = Enabled(StopInAdd());
        var cut = Render();
        FrameRows(cut)[1].Click();

        _service.DebugState = Enabled(StopInAdd(DebugStopReason.Step));
        cut.InvokeAsync(() => _service.RaiseDebugStateChanged());

        cut.WaitForAssertion(() => CollectionAssert.AreEqual(new[] { "a", "b" }, VariableNames(cut)));
    }

    // ── Controls ───────────────────────────────────────────────────────

    [TestMethod]
    public void Controls_ResumeWithTheirAction()
    {
        _service.DebugState = Enabled(StopInAdd());
        var cut = Render();

        Control(cut, UI.Debug_Continue).Click();
        Control(cut, UI.Debug_StepOver).Click();
        Control(cut, UI.Debug_StepInto).Click();
        Control(cut, UI.Debug_StepOut).Click();

        CollectionAssert.AreEqual(
            new[] { DebugResumeAction.Continue, DebugResumeAction.StepOver, DebugResumeAction.StepInto, DebugResumeAction.StepOut },
            _service.ResumeDebugCalls);
    }

    [TestMethod]
    public void Stop_CancelsTheCellThatIsRunning()
    {
        // The second cell is the one that was run; the first only defined the function the stop
        // is in. Cancelling is addressed to the run, so the outermost cell is the one named.
        _service.DebugState = Enabled(StopInAdd());
        var cut = Render();

        Control(cut, UI.Common_Stop).Click();

        CollectionAssert.AreEqual(new[] { _second.Id }, _service.CancelledCellIds);
        Assert.AreEqual(0, _service.ResumeDebugCalls.Count, "Stopping is not a way of resuming.");
    }

    // ── Breakpoints ────────────────────────────────────────────────────

    [TestMethod]
    public void Breakpoints_None_SaysSo()
    {
        _service.DebugState = Enabled();

        var cut = Render();

        Assert.AreEqual(0, cut.FindAll(".verso-debug-breakpoint-row").Count);
        StringAssert.Contains(cut.Markup, UI.Debug_Breakpoints_Empty);
    }

    [TestMethod]
    public void Breakpoints_AreListedInCellOrderThenLine()
    {
        // Set in the order the reader happened to click, which is not the order they read in.
        _service.DebugState = Enabled(breakpoints: new Dictionary<Guid, IReadOnlyList<int>>
        {
            [_third.Id] = new[] { 1 },
            [_first.Id] = new[] { 2, 4 },
        });

        var cut = Render();

        var labels = cut.FindAll(".verso-debug-breakpoint-row .verso-debug-frame-name").Select(e => e.TextContent).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            string.Format(UI.Debug_Location_CellLine, 1, 2),
            string.Format(UI.Debug_Location_CellLine, 1, 4),
            string.Format(UI.Debug_Location_CellLine, 3, 1),
        }, labels);
    }

    [TestMethod]
    public void Breakpoint_ClickingTheRow_NavigatesToItsCell()
    {
        _service.DebugState = Enabled(breakpoints: new Dictionary<Guid, IReadOnlyList<int>>
        {
            [_third.Id] = new[] { 1 },
        });
        var cut = Render();

        cut.Find(".verso-debug-breakpoint-row").Click();

        CollectionAssert.AreEqual(new[] { _third.Id }, _navigated);
    }

    [TestMethod]
    public void Breakpoint_Remove_KeepsTheCellsOtherLines()
    {
        _service.DebugState = Enabled(breakpoints: new Dictionary<Guid, IReadOnlyList<int>>
        {
            [_first.Id] = new[] { 2, 4 },
        });
        var cut = Render();

        cut.FindAll(".verso-debug-breakpoint-remove")[0].Click();

        Assert.AreEqual(1, _service.SetBreakpointsCalls.Count);
        Assert.AreEqual(_first.Id, _service.SetBreakpointsCalls[0].CellId);
        CollectionAssert.AreEqual(new[] { 4 }, _service.SetBreakpointsCalls[0].Lines.ToArray());
    }

    [TestMethod]
    public void Breakpoints_RemoveAll_OnlyOfferedForMoreThanOne()
    {
        _service.DebugState = Enabled(breakpoints: new Dictionary<Guid, IReadOnlyList<int>>
        {
            [_first.Id] = new[] { 2 },
        });

        var cut = Render();

        Assert.IsFalse(cut.FindAll("button").Any(b => b.TextContent.Trim() == UI.Debug_Breakpoints_RemoveAll),
            "Remove all beside a single breakpoint would just be its remove button again.");
    }

    [TestMethod]
    public void Breakpoints_RemoveAll_ClearsEveryCell()
    {
        _service.DebugState = Enabled(breakpoints: new Dictionary<Guid, IReadOnlyList<int>>
        {
            [_first.Id] = new[] { 2, 4 },
            [_third.Id] = new[] { 1 },
        });
        var cut = Render();

        ButtonWithText(cut, UI.Debug_Breakpoints_RemoveAll).Click();

        CollectionAssert.AreEquivalent(new[] { _first.Id, _third.Id },
            _service.SetBreakpointsCalls.Select(c => c.CellId).ToArray());
        Assert.IsTrue(_service.SetBreakpointsCalls.All(c => c.Lines.Count == 0));
    }

    // ── Updates ────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChange_ReRendersThePanel()
    {
        var cut = Render();
        Assert.IsNotNull(ButtonWithText(cut, UI.Debug_TurnOn));

        _service.DebugState = Enabled();
        cut.InvokeAsync(() => _service.RaiseDebugStateChanged());

        cut.WaitForAssertion(() =>
            Assert.AreEqual(UI.Debug_Status_Waiting, cut.Find(".verso-debug-status .verso-panel-chip").TextContent));

        _service.DebugState = Enabled(StopInAdd());
        cut.InvokeAsync(() => _service.RaiseDebugStateChanged());

        cut.WaitForAssertion(() =>
            Assert.AreEqual(UI.Debug_Status_AtBreakpoint, cut.Find(".verso-debug-status .verso-panel-chip").TextContent));
    }

    [TestMethod]
    public void NotebookChange_RenumbersTheBreakpoints()
    {
        // A cell inserted above moves every breakpoint below it down a number, even though the
        // breakpoints themselves did not change.
        _service.DebugState = Enabled(breakpoints: new Dictionary<Guid, IReadOnlyList<int>>
        {
            [_third.Id] = new[] { 1 },
        });
        var cut = Render();

        _service.Cells = new List<CellModel> { _third, _first, _second };
        cut.InvokeAsync(() => _service.RaiseNotebookChanged());

        cut.WaitForAssertion(() => Assert.AreEqual(
            string.Format(UI.Debug_Location_CellLine, 1, 1),
            cut.Find(".verso-debug-breakpoint-row .verso-debug-frame-name").TextContent));
    }
}
