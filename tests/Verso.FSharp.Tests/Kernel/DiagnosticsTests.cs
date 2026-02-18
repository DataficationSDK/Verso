using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;

[TestClass]
public class DiagnosticsTests
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

    [TestMethod]
    public async Task Diagnostics_ValidCode_ReturnsEmpty()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("let x = 10");
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task Diagnostics_UndeclaredIdentifier_ReturnsError()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("undeclaredVar + 1");

        Assert.IsTrue(diagnostics.Count > 0, "Expected at least one diagnostic.");
        Assert.IsTrue(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            "Expected an error diagnostic.");
        Assert.IsTrue(diagnostics.Any(d => d.Code == "FS0039"),
            "Expected FS0039 (undefined value) error.");
    }

    [TestMethod]
    public async Task Diagnostics_TypeMismatch_ReturnsError()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("let x: int = \"not an int\"");

        Assert.IsTrue(diagnostics.Count > 0, "Expected at least one diagnostic.");
        Assert.IsTrue(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public async Task Diagnostics_LinePositions_AreCorrect()
    {
        var code = "let x = 10\nundeclaredVar + 1";
        var diagnostics = await _kernel.GetDiagnosticsAsync(code);

        Assert.IsTrue(diagnostics.Count > 0);
        var errorDiag = diagnostics.First(d => d.Code == "FS0039");
        Assert.AreEqual(1, errorDiag.StartLine,
            "Error should be on line 1 (second line, zero-based).");
    }

    [TestMethod]
    public async Task Diagnostics_EmptyCode_ReturnsEmpty()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("");
        Assert.IsNotNull(diagnostics);
    }

    [TestMethod]
    public async Task Diagnostics_MultipleErrors_ReturnsAll()
    {
        var code = "undeclared1 + undeclared2";
        var diagnostics = await _kernel.GetDiagnosticsAsync(code);

        Assert.IsTrue(diagnostics.Count >= 2,
            $"Expected at least 2 diagnostics, got {diagnostics.Count}.");
    }

    [TestMethod]
    public async Task Diagnostics_PreviousCellContext_Respected()
    {
        // Execute a binding in a first "cell"
        await _kernel.ExecuteAsync("let myBinding = 42", _context);

        // Now check diagnostics — myBinding should be recognized
        var diagnostics = await _kernel.GetDiagnosticsAsync("myBinding + 1");
        Assert.AreEqual(0, diagnostics.Count,
            "Previously executed binding should not cause errors.");
    }

    [TestMethod]
    public async Task Diagnostics_ColumnPositions_AreCorrect()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("undeclaredVar + 1");

        Assert.IsTrue(diagnostics.Count > 0);
        var errorDiag = diagnostics.First(d => d.Code == "FS0039");
        Assert.AreEqual(0, errorDiag.StartLine);
        Assert.AreEqual(0, errorDiag.StartColumn,
            "Error should start at column 0.");
    }
}
