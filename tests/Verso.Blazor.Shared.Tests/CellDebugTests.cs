namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// A cell's share of the notebook's debugging state: whether its editor shows the breakpoint
/// margin, which lines it marks, and whether it stays on screen while it is stopped.
/// </summary>
[TestClass]
public sealed class CellDebugTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService
        {
            IsLoaded = true,
            RegisteredLanguages = new List<KernelLanguageInfo>
            {
                new("csharp", "C#", SupportsDebugging: true),
                new("fsharp", "F#"),
            },
        };
        TestContext!.Services.AddSingleton<INotebookService>(_service);
        TestContext.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CellModel CodeCell(string language = "csharp")
        => new()
        {
            Id = Guid.NewGuid(),
            Type = "code",
            Language = language,
            Source = "var x = 1;\nvar y = 2;\nvar z = x + y;",
        };

    private static DebugSnapshot Enabled(
        Dictionary<Guid, IReadOnlyList<int>>? breakpoints = null,
        DebugStop? stop = null)
        => new(true, breakpoints ?? new Dictionary<Guid, IReadOnlyList<int>>(), stop);

    private static DebugStop StopAt(Guid cellId, int line)
        => new(DebugStopReason.Breakpoint, new[]
        {
            new DebugFrame("", cellId, line, Array.Empty<DebugVariable>()),
        });

    private IRenderedComponent<Cell> RenderCell(CellModel cell, bool isSelected = false, bool collapsesInput = false)
        => RenderComponent<Cell>(p => p
            .Add(c => c.CellData, cell)
            .Add(c => c.IsSelected, isSelected)
            .Add(c => c.CollapsesInput, collapsesInput));

    private static MonacoEditor Editor(IRenderedComponent<Cell> cut)
        => cut.FindComponent<MonacoEditor>().Instance;

    // ── Gutter ─────────────────────────────────────────────────────────

    [TestMethod]
    public void DebuggingOff_NoGutter()
    {
        var cell = CodeCell();

        var editor = Editor(RenderCell(cell));

        Assert.IsFalse(editor.DebugGutter);
        Assert.AreEqual(0, editor.Breakpoints.Count);
        Assert.IsNull(editor.StoppedLine);
    }

    [TestMethod]
    public void DebuggingOn_LanguageWithoutSupport_NoGutter()
    {
        // A margin that takes clicks the kernel will never stop at would be worse than none.
        var cell = CodeCell("fsharp");
        _service.DebugState = Enabled(new Dictionary<Guid, IReadOnlyList<int>> { [cell.Id] = new[] { 2 } });

        var editor = Editor(RenderCell(cell));

        Assert.IsFalse(editor.DebugGutter);
        Assert.AreEqual(0, editor.Breakpoints.Count);
    }

    [TestMethod]
    public void DebuggingOn_LanguageNotRegistered_NoGutter()
    {
        var cell = CodeCell("ruby");
        _service.DebugState = Enabled();

        Assert.IsFalse(Editor(RenderCell(cell)).DebugGutter);
    }

    [TestMethod]
    public void DebuggingOn_SupportedLanguage_ShowsGutterWithTheCellsBreakpoints()
    {
        var cell = CodeCell();
        var other = Guid.NewGuid();
        _service.DebugState = Enabled(new Dictionary<Guid, IReadOnlyList<int>>
        {
            [cell.Id] = new[] { 1, 3 },
            [other] = new[] { 2 },
        });

        var editor = Editor(RenderCell(cell));

        Assert.IsTrue(editor.DebugGutter);
        CollectionAssert.AreEqual(new[] { 1, 3 }, editor.Breakpoints.ToArray());
        Assert.IsNull(editor.StoppedLine);
    }

    [TestMethod]
    public async Task BreakpointsEditedInTheEditor_AreSentForThisCell()
    {
        var cell = CodeCell();
        _service.DebugState = Enabled();
        var cut = RenderCell(cell);

        await cut.InvokeAsync(() => Editor(cut).OnBreakpointsEdited(new[] { 2, 3 }));

        Assert.AreEqual(1, _service.SetBreakpointsCalls.Count);
        Assert.AreEqual(cell.Id, _service.SetBreakpointsCalls[0].CellId);
        CollectionAssert.AreEqual(new[] { 2, 3 }, _service.SetBreakpointsCalls[0].Lines.ToArray());
    }

    [TestMethod]
    public void StateChange_ReachesTheEditor()
    {
        var cell = CodeCell();
        var cut = RenderCell(cell);
        Assert.IsFalse(Editor(cut).DebugGutter);

        _service.DebugState = Enabled(new Dictionary<Guid, IReadOnlyList<int>> { [cell.Id] = new[] { 2 } });
        cut.InvokeAsync(() => _service.RaiseDebugStateChanged());

        cut.WaitForAssertion(() =>
        {
            Assert.IsTrue(Editor(cut).DebugGutter);
            CollectionAssert.AreEqual(new[] { 2 }, Editor(cut).Breakpoints.ToArray());
        });
    }

    // ── Stopped ────────────────────────────────────────────────────────

    [TestMethod]
    public void StopInThisCell_MarksTheCellAndTheLine()
    {
        var cell = CodeCell();
        _service.DebugState = Enabled(stop: StopAt(cell.Id, 2));

        var cut = RenderCell(cell);

        Assert.IsTrue(cut.Find(".verso-cell").ClassList.Contains("verso-cell--stopped"));
        Assert.AreEqual(2, Editor(cut).StoppedLine);
    }

    [TestMethod]
    public void StopInAnotherCell_LeavesThisCellUnmarked()
    {
        var cell = CodeCell();
        _service.DebugState = Enabled(stop: StopAt(Guid.NewGuid(), 2));

        var cut = RenderCell(cell);

        Assert.IsFalse(cut.Find(".verso-cell").ClassList.Contains("verso-cell--stopped"));
        Assert.IsNull(Editor(cut).StoppedLine);
    }

    [TestMethod]
    public void CallerInThisCell_IsPassedToTheEditor()
    {
        // This cell called a function another cell defined, and the stop is inside it.
        var cell = CodeCell();
        _service.DebugState = Enabled(stop: new DebugStop(DebugStopReason.Step, new[]
        {
            new DebugFrame("helper", Guid.NewGuid(), 4, Array.Empty<DebugVariable>()),
            new DebugFrame("", cell.Id, 3, Array.Empty<DebugVariable>()),
        }));

        var cut = RenderCell(cell);

        Assert.IsNull(Editor(cut).StoppedLine);
        CollectionAssert.AreEqual(new[] { 3 }, Editor(cut).CallerLines.ToArray());
        Assert.IsFalse(cut.Find(".verso-cell").ClassList.Contains("verso-cell--stopped"),
            "Only the cell holding the stop itself is marked as stopped.");
    }

    [TestMethod]
    public void CollapsedInput_NotStopped_ShowsThePreviewInsteadOfTheEditor()
    {
        var cell = CodeCell();
        cell.Metadata[CellViewStateMetadata.InputCollapsedKey] = true;
        _service.DebugState = Enabled();

        var cut = RenderCell(cell);

        Assert.AreEqual(1, cut.FindAll(".verso-cell-source-preview").Count);
        Assert.AreEqual(0, cut.FindComponents<MonacoEditor>().Count);
    }

    [TestMethod]
    public void CollapsedInput_StoppedHere_StillMountsTheEditor()
    {
        // The line the cell stopped at is the thing to look at, so a collapsed cell opens up
        // for as long as it is stopped.
        var cell = CodeCell();
        cell.Metadata[CellViewStateMetadata.InputCollapsedKey] = true;
        _service.DebugState = Enabled(stop: StopAt(cell.Id, 3));

        var cut = RenderCell(cell);

        Assert.AreEqual(0, cut.FindAll(".verso-cell-source-preview").Count);
        Assert.AreEqual(3, Editor(cut).StoppedLine);
    }

    [TestMethod]
    public void PreviewMode_StoppedHere_StillMountsTheEditor()
    {
        // A layout that swaps a cell's code for its output still has to show the code of the
        // cell that is stopped.
        var cell = CodeCell();
        cell.Outputs.Add(new CellOutput("text/plain", "3"));

        var notStopped = RenderCell(cell, collapsesInput: true);
        Assert.AreEqual(0, notStopped.FindComponents<MonacoEditor>().Count,
            "Without a stop, a cell in preview mode shows only its output.");

        _service.DebugState = Enabled(stop: StopAt(cell.Id, 1));
        var stopped = RenderCell(cell, collapsesInput: true);

        Assert.AreEqual(1, stopped.FindComponents<MonacoEditor>().Count);
        Assert.AreEqual(1, Editor(stopped).StoppedLine);
    }
}
