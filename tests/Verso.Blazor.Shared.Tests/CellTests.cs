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

        var runBtn = cut.FindAll("button[title='Run']");
        Assert.IsTrue(runBtn.Count > 0);
        Assert.IsTrue(runBtn[0].HasAttribute("disabled"));
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

        Assert.IsTrue(cut.Markup.Contains("csharp"));
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

    private IRenderedComponent<Cell> RenderCell(
        CellModel cell,
        bool isSelected = false,
        bool isExecuting = false,
        int index = 0,
        bool isLast = false)
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
            .Add(c => c.IsLast, isLast));
    }
}
