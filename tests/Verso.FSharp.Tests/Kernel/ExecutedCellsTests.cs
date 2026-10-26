using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;

/// <summary>
/// FSI compiles each execution as a fragment of its own, so a cell may define again what an
/// earlier execution defined, the cell that was itself just run among them. The document the
/// kernel checks a cell in has to allow the same, or a cell that has run reports its own
/// definitions as duplicates.
/// </summary>
[TestClass]
public class ExecutedCellsTests
{
    private FSharpKernel _kernel = null!;
    private StubExecutionContext _context = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new FSharpKernel();
        await _kernel.InitializeAsync();
        _context = new StubExecutionContext();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.StartLine}:{d.StartColumn} {d.Code} {d.Message}"));

    [TestMethod]
    public async Task RunCell_CheckedAgain_HasNoDuplicateDefinitions()
    {
        var code = "module Geometry =\n    let area r = r * r\ntype Shape = Circle of float\nlet unit = 1.0";
        await _kernel.ExecuteAsync(code, _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync(code);

        Assert.AreEqual(0, diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error), Describe(diagnostics));
    }

    [TestMethod]
    public async Task RunCell_CheckedAgain_KeepsItsSemanticTokens()
    {
        var code = "type Shape = Circle of float\nlet shape = Circle 1.0";
        await _kernel.ExecuteAsync(code, _context);

        var tokens = await _kernel.GetSemanticTokensAsync(code);

        Assert.IsNotNull(tokens);
        Assert.IsTrue(tokens.Any(t => t.Line == 0 && t.StartColumn == 5 && t.Type == SemanticTokenTypes.Type),
            "the type's own declaration");
        Assert.IsTrue(tokens.Any(t => t.Line == 1 && t.StartColumn == 12 && t.Type == SemanticTokenTypes.EnumMember),
            "the union case");
    }

    [TestMethod]
    public async Task RunCell_RedefinedByAnotherCell_TheLaterDefinitionWins()
    {
        await _kernel.ExecuteAsync("let value = 1", _context);
        await _kernel.ExecuteAsync("let value = \"text\"", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let n : string = value");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task OpenInARunCell_StaysOpenForLaterCells()
    {
        await _kernel.ExecuteAsync("open System.Text", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let sb = StringBuilder()");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task ModuleFromARunCell_CanBeOpenedLater()
    {
        await _kernel.ExecuteAsync("module Geometry =\n    let area r = r * r", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("open Geometry\nlet a = area 2.0\nlet b = Geometry.area 3.0");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task OpenOfAModuleInTheSameRunCell_StaysOpenForLaterCells()
    {
        await _kernel.ExecuteAsync("module Geometry =\n    let area r = r * r\nopen Geometry", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let a = area 2.0");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task RunCellWithAReference_StillResolvesLater()
    {
        var dll = typeof(System.Text.Json.JsonSerializer).Assembly.Location;
        await _kernel.ExecuteAsync($"#r @\"{dll}\"\nlet options = System.Text.Json.JsonSerializerOptions()", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let indented = options.WriteIndented");

        Assert.AreEqual(0, diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error), Describe(diagnostics));
    }

    [TestMethod]
    public async Task RunCellWithTripleQuotedString_StillChecks()
    {
        await _kernel.ExecuteAsync("let text = \"\"\"\nline one\nline two\"\"\"", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let n = text.Length");

        Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
    }

    [TestMethod]
    public async Task OpenLineInsideATripleQuotedString_IsNotOpenedForLaterCells()
    {
        await _kernel.ExecuteAsync("let text = \"\"\"\nopen System.Text\n\"\"\"", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let sb = StringBuilder()");

        Assert.IsTrue(diagnostics.Any(d => d.Code == "FS0039"), Describe(diagnostics));
    }

    [TestMethod]
    public async Task OpenLineInsideAVerbatimString_IsNotOpenedForLaterCells()
    {
        await _kernel.ExecuteAsync("let text = @\"\nopen System.Text\n\"", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let sb = StringBuilder()");

        Assert.IsTrue(diagnostics.Any(d => d.Code == "FS0039"), Describe(diagnostics));
    }

    [TestMethod]
    public async Task OpenLineInsideABlockComment_IsNotOpenedForLaterCells()
    {
        await _kernel.ExecuteAsync("(*\nopen System.Text\n*)\nlet value = 1", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let sb = StringBuilder()");

        Assert.IsTrue(diagnostics.Any(d => d.Code == "FS0039"), Describe(diagnostics));
    }

    [TestMethod]
    public async Task OpenInABranchFsiLeavesOut_IsNotOpenedForLaterCells()
    {
        await _kernel.ExecuteAsync("#if INTERACTIVE\nlet mode = \"fsi\"\n#else\nopen System.Text\n#endif", _context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("let m : string = mode\nlet sb = StringBuilder()");

        Assert.AreEqual(1, diagnostics.Count, Describe(diagnostics));
        Assert.AreEqual("FS0039", diagnostics[0].Code, Describe(diagnostics));
        Assert.AreEqual(1, diagnostics[0].StartLine, Describe(diagnostics));
    }

    [TestMethod]
    public void DirectiveLineInsideAMultiLineString_StaysInTheString()
    {
        var context = new FSharpProjectContext(Array.Empty<string>(), Array.Empty<string>());
        context.AppendExecutedCode("let text = \"\"\"\n#r \"nothing.dll\"\nopen Nowhere\n\"\"\"");

        var (sourceText, _, _) = context.BuildDocument("");
        var lines = sourceText.Replace("\r\n", "\n").Split('\n');

        CollectionAssert.Contains(lines, "    #r \"nothing.dll\"", sourceText);
        CollectionAssert.DoesNotContain(lines, "#r \"nothing.dll\"", sourceText);
        CollectionAssert.DoesNotContain(lines, "open Nowhere", sourceText);
    }

    [TestMethod]
    public async Task Completions_DoNotOfferTheExecutedCellModules()
    {
        await _kernel.ExecuteAsync("let value = 1", _context);

        var completions = await _kernel.GetCompletionsAsync("let x = valu", 12);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "value"), "the earlier cell's binding");
        Assert.IsFalse(completions.Any(c => c.DisplayText.Contains("Verso", StringComparison.OrdinalIgnoreCase)
                                             && c.Kind == "Module"),
            string.Join(", ", completions.Where(c => c.Kind == "Module").Select(c => c.DisplayText)));
    }

    [TestMethod]
    public async Task HoverAndDiagnostics_DoNotNameTheExecutedCellModules()
    {
        await _kernel.ExecuteAsync("module Geometry =\n    let area r = r * r\ntype Shape = Circle of float", _context);

        var code = "let a = Geometry.area 2.0\nlet s : Shape = 1";
        var hover = await _kernel.GetHoverInfoAsync(code, code.IndexOf("Geometry", StringComparison.Ordinal) + 1);
        var diagnostics = await _kernel.GetDiagnosticsAsync(code);

        Assert.IsNotNull(hover);
        StringAssert.DoesNotMatch(hover.Content, new System.Text.RegularExpressions.Regex(FSharpProjectContext.ExecutedCellModulePrefix));
        Assert.IsTrue(diagnostics.Count > 0, "the int assigned to a Shape");
        foreach (var d in diagnostics)
            StringAssert.DoesNotMatch(d.Message, new System.Text.RegularExpressions.Regex(FSharpProjectContext.ExecutedCellModulePrefix));
    }

    [TestMethod]
    public void HideExecutedCellModules_KeepsTheRestOfTheName()
    {
        Assert.AreEqual("module Geometry from Notebook",
            FSharpProjectContext.HideExecutedCellModules("module Geometry from Notebook.VersoExecutedCell1"));
        Assert.AreEqual("expected 'Geometry.Shape'",
            FSharpProjectContext.HideExecutedCellModules("expected 'VersoExecutedCell12.Geometry.Shape'"));
    }
}
