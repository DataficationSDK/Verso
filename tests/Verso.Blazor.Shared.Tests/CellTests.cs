namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class CellTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService { IsLoaded = true };
        TestContext!.Services.AddSingleton<INotebookService>(_service);
    }

    [TestMethod]
    public void CssClass_ReflectsIsSelected()
    {
        var cell = CreateCodeCell("var x = 1;");

        var cut = RenderCell(cell, isSelected: true);

        Assert.IsTrue(cut.Markup.Contains("verso-cell--selected") || cut.Markup.Contains("selected"));
    }

    [TestMethod]
    public void CssClass_ReflectsNotSelected()
    {
        var cell = CreateCodeCell("var x = 1;");

        var cut = RenderCell(cell, isSelected: false);

        Assert.IsFalse(cut.Markup.Contains("verso-cell--selected"));
    }

    [TestMethod]
    public void CssClass_ReflectsIsExecuting()
    {
        var cell = CreateCodeCell("var x = 1;");

        var cut = RenderCell(cell, isExecuting: true);

        Assert.IsTrue(cut.Markup.Contains("executing") || cut.Markup.Contains("spinner") || cut.Markup.Contains("verso-spinner"));
    }

    [TestMethod]
    public void CellIndex_Displayed()
    {
        var cell = CreateCodeCell("code");

        var cut = RenderCell(cell, index: 3);

        Assert.IsTrue(cut.Markup.Contains("3"));
    }

    [TestMethod]
    public void MoveUp_Hidden_WhenFirstCell()
    {
        var cell = CreateCodeCell("first");

        var cut = RenderCell(cell, index: 0);

        var moveUpBtns = cut.FindAll("button[title='Move Up']");
        Assert.AreEqual(0, moveUpBtns.Count);
    }

    [TestMethod]
    public void MoveUp_Visible_WhenNotFirstCell()
    {
        var cell = CreateCodeCell("second");

        var cut = RenderCell(cell, index: 1);

        var moveUpBtns = cut.FindAll("button[title='Move Up']");
        Assert.IsTrue(moveUpBtns.Count > 0);
    }

    [TestMethod]
    public void MoveDown_Hidden_WhenLastCell()
    {
        var cell = CreateCodeCell("last");

        var cut = RenderCell(cell, index: 0, isLast: true);

        var moveDownBtns = cut.FindAll("button[title='Move Down']");
        Assert.AreEqual(0, moveDownBtns.Count);
    }

    [TestMethod]
    public void MoveDown_Visible_WhenNotLastCell()
    {
        var cell = CreateCodeCell("middle");

        var cut = RenderCell(cell, index: 0, isLast: false);

        var moveDownBtns = cut.FindAll("button[title='Move Down']");
        Assert.IsTrue(moveDownBtns.Count > 0);
    }

    [TestMethod]
    public void RunButton_DisabledDuringExecution()
    {
        var cell = CreateCodeCell("running");

        var cut = RenderCell(cell, isExecuting: true);

        // For kernels that support cancellation (csharp does), the Run button
        // is replaced by a Stop button while executing. Otherwise it remains
        // visible but disabled.
        var stopBtn = cut.FindAll("button[title='Stop']");
        if (stopBtn.Count > 0)
        {
            Assert.IsFalse(stopBtn[0].HasAttribute("disabled"));
        }
        else
        {
            var runBtn = cut.FindAll("button[title='Run']");
            Assert.IsTrue(runBtn.Count > 0);
            Assert.IsTrue(runBtn[0].HasAttribute("disabled"));
        }
    }

    [TestMethod]
    public void RunButton_EnabledWhenNotExecuting()
    {
        var cell = CreateCodeCell("ready");

        var cut = RenderCell(cell, isExecuting: false);

        var runBtn = cut.FindAll("button[title='Run']");
        Assert.IsTrue(runBtn.Count > 0);
        Assert.IsFalse(runBtn[0].HasAttribute("disabled"));
    }

    [TestMethod]
    public void Outputs_Rendered_WhenPresent()
    {
        var cell = CreateCodeCell("print('hello')");
        cell.Outputs.Add(new CellOutput("text/plain", "hello"));

        var cut = RenderCell(cell);

        Assert.IsTrue(cut.Markup.Contains("hello"));
    }

    [TestMethod]
    public void ErrorOutput_RenderedWithErrorClass()
    {
        var cell = CreateCodeCell("throw new Exception();");
        cell.Outputs.Add(new CellOutput("text/plain", "NullReferenceException",
            IsError: true, ErrorName: "NullReferenceException"));

        var cut = RenderCell(cell);

        Assert.IsTrue(cut.Markup.Contains("error") || cut.Markup.Contains("Error"));
        Assert.IsTrue(cut.Markup.Contains("NullReferenceException"));
    }

    [TestMethod]
    public void HtmlOutput_RenderedAsMarkup()
    {
        var cell = CreateCodeCell("display(html)");
        cell.Outputs.Add(new CellOutput("text/html", "<b>bold text</b>"));

        var cut = RenderCell(cell);

        Assert.IsTrue(cut.Markup.Contains("<b>bold text</b>"));
    }

    [TestMethod]
    public void DuplicateHtmlOutputs_RenderWithoutKeyCollision()
    {
        // Two value-equal outputs in one cell (e.g. #!import --show-output where two imported
        // cells produce the same result table) must not collide on @key: CellOutput is a record
        // with value equality, and a duplicate key throws in the render tree diff and kills the
        // circuit, leaving the notebook stuck in the running state.
        var cell = CreateCodeCell("#!import ImportCall.verso --show-output");
        cell.Outputs.Add(new CellOutput("text/html", "<table><tr><td>same</td></tr></table>"));

        var cut = RenderCell(cell);

        cell.Outputs.Add(new CellOutput("text/html", "<table><tr><td>same</td></tr></table>"));
        cut.Render(); // re-render with the duplicate present; must not throw

        Assert.AreEqual(2, cut.FindAll(".verso-output--html").Count);
    }

    [TestMethod]
    public void ErrorOutput_ShowsStackTrace()
    {
        var cell = CreateCodeCell("fail");
        cell.Outputs.Add(new CellOutput("text/plain", "Error occurred",
            IsError: true, ErrorName: "Exception", ErrorStackTrace: "at Line 1"));

        var cut = RenderCell(cell);

        Assert.IsTrue(cut.Markup.Contains("at Line 1"));
    }

    [TestMethod]
    public void DeleteButton_Present()
    {
        var cell = CreateCodeCell("code");

        var cut = RenderCell(cell);

        var deleteBtns = cut.FindAll("button[title='Delete']");
        Assert.IsTrue(deleteBtns.Count > 0);
    }

    [TestMethod]
    public void LanguageLabel_ShownForCodeCells()
    {
        var cell = CreateCodeCell("code");
        cell.Language = "csharp";

        var cut = RenderCell(cell);

        Assert.IsTrue(cut.Markup.Contains("C#"));
    }

    [TestMethod]
    public void RenderOnlyCell_Deselected_ShowsRenderedOutput()
    {
        // A deselected Markdown cell shows its rendered output (preview mode).
        var cell = CreateMarkdownCell("# Heading");
        cell.Outputs.Add(new CellOutput("text/html", "<em>rendered-md</em>"));

        var cut = RenderCell(cell, isSelected: false, collapsesInput: true);

        Assert.IsTrue(cut.Markup.Contains("<em>rendered-md</em>"),
            "A deselected render-only cell should show its rendered output.");
    }

    [TestMethod]
    public void RenderOnlyCell_Selected_HidesOutput_ShowsEditorOnly()
    {
        // Selecting a Markdown cell enters edit mode: only the editor shows, not the
        // rendered output, matching Jupyter/Polyglot.
        var cell = CreateMarkdownCell("# Heading");
        cell.Outputs.Add(new CellOutput("text/html", "<em>rendered-md</em>"));

        var cut = RenderCell(cell, isSelected: true, collapsesInput: true);

        Assert.IsFalse(cut.Markup.Contains("<em>rendered-md</em>"),
            "A render-only cell being edited should not show its rendered output alongside the editor.");
    }

    [TestMethod]
    public void CodeCell_Selected_StillShowsOutput()
    {
        // Code cell output is independent of selection and always shows.
        var cell = CreateCodeCell("print('x')");
        cell.Outputs.Add(new CellOutput("text/plain", "code-output"));

        var cut = RenderCell(cell, isSelected: true, collapsesInput: false);

        Assert.IsTrue(cut.Markup.Contains("code-output"),
            "A code cell's output should remain visible while it is selected.");
    }

    [TestMethod]
    public void CodeCell_EditingLayout_MountsEditor()
    {
        // The default fake layout advertises CellEdit, so an editable code cell mounts its editor.
        var cell = CreateCodeCell("var x = 1;");

        var cut = RenderCell(cell);

        Assert.IsTrue(cut.FindAll(".verso-cell-editor").Count > 0,
            "An editing layout should mount the code editor.");
    }

    [TestMethod]
    public void CodeCell_DisplayOnlyLayout_OmitsEditor()
    {
        // A read-only display layout (Presentation, Dashboard) advertises no CellEdit capability
        // and hides the editor with CSS anyway, so the cell should not mount a Monaco editor at all.
        _service.LayoutCapabilities = LayoutCapabilities.None;
        var cell = CreateCodeCell("var x = 1;");

        var cut = RenderCell(cell);

        Assert.AreEqual(0, cut.FindAll(".verso-cell-editor").Count,
            "A read-only display layout should not mount the code editor.");
    }

    [TestMethod]
    public void CodeCell_DisplayOnlyLayout_StillShowsOutput()
    {
        // Suppressing the editor must not suppress the output the reader came to see.
        _service.LayoutCapabilities = LayoutCapabilities.None;
        var cell = CreateCodeCell("print('x')");
        cell.Outputs.Add(new CellOutput("text/plain", "code-output"));

        var cut = RenderCell(cell);

        Assert.IsTrue(cut.Markup.Contains("code-output"),
            "A code cell's output should remain visible in a read-only display layout.");
        Assert.AreEqual(0, cut.FindAll(".verso-cell-editor").Count);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static CellModel CreateCodeCell(string source)
    {
        return new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "code",
            Language = "csharp",
            Source = source
        };
    }

    private static CellModel CreateMarkdownCell(string source)
    {
        return new CellModel
        {
            Id = Guid.NewGuid(),
            Type = "markdown",
            Source = source
        };
    }

    [TestMethod]
    public void CellToolbar_QueriesEnabledStates_OnlyWhenInputsChange()
    {
        // A non-hardcoded cell-toolbar action (like the built-in Clear Output) opens the enabled-state
        // path; run-cell is hardcoded and would not.
        _service.ToolbarActions.Add(new ToolbarActionInfo(
            "verso.action.clear-cell-output", "Clear Output", null, ToolbarPlacement.CellToolbar, 31));
        _service.ActionEnabledStates["verso.action.clear-cell-output"] = true;

        var cell = CreateCodeCell("var x = 1;");
        var cut = RenderCell(cell);

        // Initial render queries the host exactly once.
        Assert.AreEqual(1, _service.GetActionEnabledStatesCallCount);

        // Re-supplying identical parameters (what the parent does on every whole-notebook re-render,
        // e.g. once per streamed output during Run All) must not re-query the host.
        cut.SetParametersAndRender(p => p.Add(c => c.IsSelected, false));
        cut.SetParametersAndRender(p => p.Add(c => c.IsSelected, false));
        cut.SetParametersAndRender(p => p.Add(c => c.IsSelected, false));
        Assert.AreEqual(1, _service.GetActionEnabledStatesCallCount);

        // Changing an input that affects enablement (selection) re-queries exactly once more.
        cut.SetParametersAndRender(p => p.Add(c => c.IsSelected, true));
        Assert.AreEqual(2, _service.GetActionEnabledStatesCallCount);
    }

    [TestMethod]
    public void NonEditableCell_AutoRenders_Once_AcrossReRenders()
    {
        // Loose JS interop so the non-editable cell's OnAfterRender lifecycle runs without stubbing
        // every JS call; the auto-render path under test is pure C#.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;

        var cell = CreateCodeCell(string.Empty);
        var runCount = 0;

        var cut = RenderComponent<Cell>(p => p
            .Add(c => c.CellData, cell)
            .Add(c => c.IsEditable, false)
            .Add(c => c.OnRunCell, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<Guid>(this, _ => runCount++)));

        // A non-editable cell with no outputs (e.g. the parameters form) auto-renders exactly once on open.
        Assert.AreEqual(1, runCount);

        // Re-renders that occur before the first output arrives (outputs still empty, and a render-only
        // cell never reports an executing state) must NOT re-trigger execution; otherwise the cell spins
        // and can lock the UI. This is the regression the issue #83 fixes must not introduce.
        cut.SetParametersAndRender(p => p.Add(c => c.IsSelected, false));
        cut.SetParametersAndRender(p => p.Add(c => c.IsSelected, true));
        cut.SetParametersAndRender(p => p.Add(c => c.IsSelected, false));
        Assert.AreEqual(1, runCount);
    }

    private IRenderedComponent<Cell> RenderCell(
        CellModel cell,
        bool isSelected = false,
        bool isExecuting = false,
        int index = 0,
        bool isLast = false,
        bool collapsesInput = false)
    {
        // Set up JS interop stub for MonacoEditor
        TestContext!.JSInterop.SetupVoid("versoMonaco.create", _ => true);
        TestContext.JSInterop.SetupVoid("versoMonaco.setValue", _ => true);
        TestContext.JSInterop.SetupVoid("versoMonaco.setLanguage", _ => true);
        TestContext.JSInterop.SetupVoid("versoMonaco.dispose", _ => true);

        return RenderComponent<Cell>(p => p
            .Add(c => c.CellData, cell)
            .Add(c => c.IsSelected, isSelected)
            .Add(c => c.IsExecuting, isExecuting)
            .Add(c => c.Index, index)
            .Add(c => c.IsLast, isLast)
            .Add(c => c.CollapsesInput, collapsesInput));
    }
}
