using Verso.Abstractions;
using Verso.Extensions.CellTypes;
using Verso.Extensions.Kernels;
using Verso.Extensions.Renderers;
using Verso.Testing.Stubs;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class MermaidKernelTests
{
    private readonly MermaidKernel _kernel = new();
    private readonly StubExecutionContext _context = new();

    // --- Kernel execution ---

    [TestMethod]
    public async Task Execute_ProducesMermaidMimeType()
    {
        var outputs = await _kernel.ExecuteAsync("graph TD\n    A-->B", _context);
        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("text/x-verso-mermaid", outputs[0].MimeType);
        Assert.AreEqual("graph TD\n    A-->B", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_VariableSubstitution_ReplacesTokens()
    {
        _context.Variables.Set("target", "C");
        var outputs = await _kernel.ExecuteAsync("graph TD\n    A-->@target", _context);
        Assert.AreEqual("graph TD\n    A-->C", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_DoubleAtEscape_ProducesLiteralAt()
    {
        var outputs = await _kernel.ExecuteAsync("graph TD\n    A[@@sign]-->B", _context);
        Assert.AreEqual("graph TD\n    A[@sign]-->B", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_UnresolvedVariable_LeavesAsIs()
    {
        var outputs = await _kernel.ExecuteAsync("graph TD\n    A-->@missing", _context);
        Assert.AreEqual("graph TD\n    A-->@missing", outputs[0].Content);
    }

    // --- Completions ---

    [TestMethod]
    public async Task Completions_IncludesDiagramKeywords()
    {
        await _kernel.ExecuteAsync("", _context);
        var completions = await _kernel.GetCompletionsAsync("flow", 4);
        Assert.IsTrue(completions.Any(c => c.InsertText == "flowchart"));
    }

    [TestMethod]
    public async Task Completions_IncludesGraphKeyword()
    {
        await _kernel.ExecuteAsync("", _context);
        var completions = await _kernel.GetCompletionsAsync("g", 1);
        Assert.IsTrue(completions.Any(c => c.InsertText == "graph"));
    }

    [TestMethod]
    public async Task Completions_IncludesSequenceDiagram()
    {
        await _kernel.ExecuteAsync("", _context);
        var completions = await _kernel.GetCompletionsAsync("seq", 3);
        Assert.IsTrue(completions.Any(c => c.InsertText == "sequenceDiagram"));
    }

    [TestMethod]
    public async Task Completions_OffersVariables()
    {
        _context.Variables.Set("nodeLabel", "Test");
        await _kernel.ExecuteAsync("", _context);

        var completions = await _kernel.GetCompletionsAsync("@n", 2);
        Assert.IsTrue(completions.Any(c => c.InsertText == "@nodeLabel"));
    }

    [TestMethod]
    public async Task Completions_SkipsInternalVariables()
    {
        _context.Variables.Set("__verso_internal", "hidden");
        _context.Variables.Set("visible", "shown");
        await _kernel.ExecuteAsync("", _context);

        var completions = await _kernel.GetCompletionsAsync("@", 1);
        Assert.IsFalse(completions.Any(c => c.InsertText.Contains("__verso_")));
    }

    // --- Diagnostics ---

    [TestMethod]
    public async Task Diagnostics_WarnsOnUnresolvedVariable()
    {
        await _kernel.ExecuteAsync("", _context);
        var diagnostics = await _kernel.GetDiagnosticsAsync("graph TD\n    A-->@missing");
        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("@missing"));
    }

    [TestMethod]
    public async Task Diagnostics_NoDiagnosticsForResolvedVariables()
    {
        _context.Variables.Set("target", "B");
        await _kernel.ExecuteAsync("", _context);
        var diagnostics = await _kernel.GetDiagnosticsAsync("graph TD\n    A-->@target");
        Assert.AreEqual(0, diagnostics.Count);
    }

    // --- Hover ---

    [TestMethod]
    public async Task Hover_ShowsVariableInfo()
    {
        _context.Variables.Set("label", "Hello");
        await _kernel.ExecuteAsync("", _context);
        var hover = await _kernel.GetHoverInfoAsync("@label", 3);
        Assert.IsNotNull(hover);
        Assert.IsTrue(hover.Content.Contains("label"));
        Assert.IsTrue(hover.Content.Contains("String"));
    }

    [TestMethod]
    public async Task Hover_ReturnsNullForNonVariable()
    {
        await _kernel.ExecuteAsync("", _context);
        var hover = await _kernel.GetHoverInfoAsync("graph TD", 3);
        Assert.IsNull(hover);
    }

    // --- Kernel properties ---

    [TestMethod]
    public void LanguageId_IsMermaid()
        => Assert.AreEqual("mermaid", _kernel.LanguageId);

    [TestMethod]
    public void DisplayName_IsMermaid()
        => Assert.AreEqual("Mermaid", _kernel.DisplayName);

    [TestMethod]
    public void FileExtensions_ContainsMmdAndMermaid()
    {
        Assert.IsTrue(_kernel.FileExtensions.Contains(".mmd"));
        Assert.IsTrue(_kernel.FileExtensions.Contains(".mermaid"));
    }

    // --- Cell type properties ---

    [TestMethod]
    public void CellType_HasCorrectId()
    {
        var cellType = new MermaidCellType();
        Assert.AreEqual("mermaid", cellType.CellTypeId);
    }

    [TestMethod]
    public void CellType_HasKernel()
    {
        var cellType = new MermaidCellType();
        Assert.IsNotNull(cellType.Kernel);
    }

    [TestMethod]
    public void CellType_IsEditable()
    {
        var cellType = new MermaidCellType();
        Assert.IsTrue(cellType.IsEditable);
    }

    // --- Renderer properties ---

    [TestMethod]
    public void Renderer_CollapsesInputOnExecute()
    {
        var renderer = new MermaidCellRenderer();
        Assert.IsTrue(renderer.CollapsesInputOnExecute);
    }

    [TestMethod]
    public void Renderer_EditorLanguageIsMermaid()
    {
        var renderer = new MermaidCellRenderer();
        Assert.AreEqual("mermaid", renderer.GetEditorLanguage());
    }

    [TestMethod]
    public async Task Renderer_RenderOutput_PassesThrough()
    {
        var renderer = new MermaidCellRenderer();
        var renderContext = new StubCellRenderContext();
        var output = new CellOutput("text/x-verso-mermaid", "graph TD\n    A-->B");
        var result = await renderer.RenderOutputAsync(output, renderContext);
        Assert.AreEqual("text/x-verso-mermaid", result.MimeType);
        Assert.AreEqual("graph TD\n    A-->B", result.Content);
    }
}
