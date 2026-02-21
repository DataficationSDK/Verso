using Verso.Abstractions;
using Verso.Extensions.CellTypes;
using Verso.Extensions.Kernels;
using Verso.Extensions.Renderers;
using Verso.Testing.Stubs;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class HtmlKernelTests
{
    private readonly HtmlKernel _kernel = new();
    private readonly StubExecutionContext _context = new();

    // --- Kernel execution ---

    [TestMethod]
    public async Task Execute_ProducesTextHtmlOutput()
    {
        var outputs = await _kernel.ExecuteAsync("<h1>Hello</h1>", _context);
        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("text/html", outputs[0].MimeType);
        Assert.AreEqual("<h1>Hello</h1>", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_VariableSubstitution_ReplacesTokens()
    {
        _context.Variables.Set("name", "World");
        var outputs = await _kernel.ExecuteAsync("<h1>Hello @name</h1>", _context);
        Assert.AreEqual("<h1>Hello World</h1>", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_DoubleAtEscape_ProducesLiteralAt()
    {
        var outputs = await _kernel.ExecuteAsync("<p>@@escaped</p>", _context);
        Assert.AreEqual("<p>@escaped</p>", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_UnresolvedVariable_LeavesAsIs()
    {
        var outputs = await _kernel.ExecuteAsync("<p>@missing</p>", _context);
        Assert.AreEqual("<p>@missing</p>", outputs[0].Content);
    }

    // --- Completions ---

    [TestMethod]
    public async Task Completions_OffersVariables()
    {
        _context.Variables.Set("title", "Test");
        // Execute first to capture variable store
        await _kernel.ExecuteAsync("", _context);

        var completions = await _kernel.GetCompletionsAsync("@t", 2);
        Assert.IsTrue(completions.Any(c => c.InsertText == "@title"));
    }

    [TestMethod]
    public async Task Completions_SkipsInternalVariables()
    {
        _context.Variables.Set("__verso_internal", "hidden");
        _context.Variables.Set("visible", "shown");
        await _kernel.ExecuteAsync("", _context);

        var completions = await _kernel.GetCompletionsAsync("@", 1);
        Assert.IsFalse(completions.Any(c => c.InsertText.Contains("__verso_")));
        Assert.IsTrue(completions.Any(c => c.InsertText == "@visible"));
    }

    // --- Diagnostics ---

    [TestMethod]
    public async Task Diagnostics_WarnsOnUnresolvedVariable()
    {
        await _kernel.ExecuteAsync("", _context);
        var diagnostics = await _kernel.GetDiagnosticsAsync("<p>@missing</p>");
        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("@missing"));
    }

    [TestMethod]
    public async Task Diagnostics_NoDiagnosticsForResolvedVariables()
    {
        _context.Variables.Set("name", "World");
        await _kernel.ExecuteAsync("", _context);
        var diagnostics = await _kernel.GetDiagnosticsAsync("<h1>@name</h1>");
        Assert.AreEqual(0, diagnostics.Count);
    }

    // --- Hover ---

    [TestMethod]
    public async Task Hover_ShowsVariableInfo()
    {
        _context.Variables.Set("title", "Hello");
        await _kernel.ExecuteAsync("", _context);
        var hover = await _kernel.GetHoverInfoAsync("@title", 3);
        Assert.IsNotNull(hover);
        Assert.IsTrue(hover.Content.Contains("title"));
        Assert.IsTrue(hover.Content.Contains("String"));
    }

    [TestMethod]
    public async Task Hover_ReturnsNullForNonVariable()
    {
        await _kernel.ExecuteAsync("", _context);
        var hover = await _kernel.GetHoverInfoAsync("plain text", 5);
        Assert.IsNull(hover);
    }

    // --- Kernel properties ---

    [TestMethod]
    public void LanguageId_IsHtml()
        => Assert.AreEqual("html", _kernel.LanguageId);

    [TestMethod]
    public void DisplayName_IsHtml()
        => Assert.AreEqual("HTML", _kernel.DisplayName);

    [TestMethod]
    public void FileExtensions_ContainsHtmlAndHtm()
    {
        Assert.IsTrue(_kernel.FileExtensions.Contains(".html"));
        Assert.IsTrue(_kernel.FileExtensions.Contains(".htm"));
    }

    // --- Cell type properties ---

    [TestMethod]
    public void CellType_HasCorrectId()
    {
        var cellType = new HtmlCellType();
        Assert.AreEqual("html", cellType.CellTypeId);
    }

    [TestMethod]
    public void CellType_HasKernel()
    {
        var cellType = new HtmlCellType();
        Assert.IsNotNull(cellType.Kernel);
    }

    [TestMethod]
    public void CellType_IsEditable()
    {
        var cellType = new HtmlCellType();
        Assert.IsTrue(cellType.IsEditable);
    }

    // --- Renderer properties ---

    [TestMethod]
    public void Renderer_CollapsesInputOnExecute()
    {
        var renderer = new HtmlCellRenderer();
        Assert.IsTrue(renderer.CollapsesInputOnExecute);
    }

    [TestMethod]
    public void Renderer_EditorLanguageIsHtml()
    {
        var renderer = new HtmlCellRenderer();
        Assert.AreEqual("html", renderer.GetEditorLanguage());
    }

    [TestMethod]
    public async Task Renderer_RenderOutput_PassesThrough()
    {
        var renderer = new HtmlCellRenderer();
        var renderContext = new StubCellRenderContext();
        var output = new CellOutput("text/html", "<p>hello</p>");
        var result = await renderer.RenderOutputAsync(output, renderContext);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.AreEqual("<p>hello</p>", result.Content);
    }
}
