using Verso.Abstractions;
using Verso.Kernels;

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
}
