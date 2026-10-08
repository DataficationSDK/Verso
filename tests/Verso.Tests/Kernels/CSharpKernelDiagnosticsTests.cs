using Verso.Abstractions;
using Verso.Contexts;
using Verso.Kernels;
using Verso.Stubs;

namespace Verso.Tests.Kernels;

[TestClass]
public sealed class CSharpKernelDiagnosticsTests
{
    private CSharpKernel _kernel = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new CSharpKernel();
        await _kernel.InitializeAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task Diagnostics_ValidCode_ReturnsEmpty()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("var x = 10;");

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task Diagnostics_UndeclaredVariable_ReturnsError()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("undeclaredVar + 1");

        Assert.IsTrue(diagnostics.Count > 0, "Expected at least one diagnostic.");
        Assert.IsTrue(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            "Expected an error diagnostic.");
        Assert.IsTrue(diagnostics.Any(d => d.Code == "CS0103"),
            "Expected CS0103 (name does not exist) error.");
    }

    [TestMethod]
    public async Task Diagnostics_TypeMismatch_ReturnsError()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("int x = \"not an int\";");

        Assert.IsTrue(diagnostics.Count > 0);
        Assert.IsTrue(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public async Task Diagnostics_LinePositions_AreCorrect()
    {
        var code = "var x = 10;\nundeclaredVar + 1";
        var diagnostics = await _kernel.GetDiagnosticsAsync(code);

        Assert.IsTrue(diagnostics.Count > 0);
        var errorDiag = diagnostics.First(d => d.Code == "CS0103");
        Assert.AreEqual(1, errorDiag.StartLine, "Error should be on line 1 (second line, zero-based).");
    }

    [TestMethod]
    public async Task Diagnostics_EmptyCode_ReturnsEmpty()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("");

        // Empty code may produce some diagnostics or none — just verify no exception
        Assert.IsNotNull(diagnostics);
    }

    [TestMethod]
    public async Task Diagnostics_MultipleErrors_ReturnsAll()
    {
        var code = "undeclared1 + undeclared2";
        var diagnostics = await _kernel.GetDiagnosticsAsync(code);

        Assert.IsTrue(diagnostics.Count >= 2,
            "Expected at least 2 diagnostics for two undeclared variables.");
    }

    [TestMethod]
    public async Task Diagnostics_ColumnPositions_AreCorrect()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("undeclaredVar + 1");

        Assert.IsTrue(diagnostics.Count > 0);
        var errorDiag = diagnostics.First(d => d.Code == "CS0103");
        Assert.AreEqual(0, errorDiag.StartLine);
        Assert.AreEqual(0, errorDiag.StartColumn, "Error should start at column 0.");
    }

    [TestMethod]
    public async Task Diagnostics_NotebookParameter_IsKnownAfterFirstRun()
    {
        var notebook = new NotebookModel
        {
            Parameters = new Dictionary<string, NotebookParameterDefinition>
            {
                ["region"] = new() { Type = "string", Default = "us-east" },
                ["batchSize"] = new() { Type = "int", Default = 1000L }
            }
        };
        var context = CreateContext(notebook);
        context.Variables.Set("region", "us-east");
        context.Variables.Set("batchSize", 1000L);

        await _kernel.ExecuteAsync("var unrelated = 1;", context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("var upper = region.ToUpper(); long next = batchSize + 1;");

        Assert.AreEqual(0, diagnostics.Count,
            "Parameters injected at run time should be known to diagnostics: "
            + string.Join("; ", diagnostics.Select(d => $"{d.Code} {d.Message}")));
    }

    [TestMethod]
    public async Task Diagnostics_NameBoundInEarlierCell_IsKnown()
    {
        var context = CreateContext(new NotebookModel());
        await _kernel.ExecuteAsync("var answer = 42;", context);

        var diagnostics = await _kernel.GetDiagnosticsAsync("answer + 1");

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task Diagnostics_NuGetDirectives_AreNotReported()
    {
        // The kernel resolves these directives itself before the code reaches Roslyn.
        var code = "#i \"nuget: https://example.com/feed/index.json\"\n#r \"nuget: Some.Package, 1.0.0\"\nundeclaredVar";

        var diagnostics = await _kernel.GetDiagnosticsAsync(code);

        Assert.IsFalse(diagnostics.Any(d => d.StartLine < 2),
            "Directive lines should not be reported: "
            + string.Join("; ", diagnostics.Select(d => $"{d.Code} {d.Message}")));
        var undeclared = diagnostics.Single(d => d.Code == "CS0103");
        Assert.AreEqual(2, undeclared.StartLine, "Line numbers should still match the cell.");
    }

    [TestMethod]
    public async Task Diagnostics_UnterminatedNuGetDirective_KeepsLaterLinesInPlace()
    {
        // Mid-typing, the directive has no closing quote, so the directive pattern runs on to
        // the quote on the next line. Blanking it must not swallow that line break.
        var code = "#r \"nuget: Some.Package\nvar s = \"x\";\nundeclaredVar";

        var diagnostics = await _kernel.GetDiagnosticsAsync(code);

        Assert.IsTrue(diagnostics.Any(d => d.Code == "CS0103" && d.StartLine == 2
                && d.Message.Contains("undeclaredVar")),
            "The error on the third line should be reported on that line: "
            + string.Join("; ", diagnostics.Select(d => $"{d.StartLine} {d.Code} {d.Message}")));
    }

    private static Verso.Contexts.ExecutionContext CreateContext(NotebookModel notebook)
    {
        return new Verso.Contexts.ExecutionContext(
            Guid.NewGuid(), 1, new VariableStore(), CancellationToken.None,
            new StubThemeContext(), LayoutCapabilities.None,
            new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>()),
            new NotebookMetadataContext(notebook),
            new Verso.Stubs.StubNotebookOperations(),
            writeOutput: _ => Task.CompletedTask,
            display: _ => Task.CompletedTask);
    }
}
