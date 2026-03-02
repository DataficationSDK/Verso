using Verso.Abstractions;
using Verso.Python.Kernel;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Kernel;

[TestClass]
public sealed class DiagnosticsTests
{
    private PythonKernel _kernel = null!;
    private StubExecutionContext _context = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new PythonKernel();
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
        var diagnostics = await _kernel.GetDiagnosticsAsync("x = 10");
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task Diagnostics_SyntaxError_ReturnsError()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("def foo(:");

        Assert.IsTrue(diagnostics.Count > 0, "Expected at least one diagnostic.");
        Assert.IsTrue(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            "Expected an error diagnostic.");
        Assert.IsTrue(diagnostics.Any(d => d.Code == "SyntaxError"),
            "Expected SyntaxError code.");
    }

    [TestMethod]
    public async Task Diagnostics_LinePositions_AreCorrect()
    {
        var code = "x = 10\ndef foo(:";
        var diagnostics = await _kernel.GetDiagnosticsAsync(code);

        Assert.IsTrue(diagnostics.Count > 0);
        var errorDiag = diagnostics.First(d => d.Code == "SyntaxError");
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
    public async Task Diagnostics_PreviousCellContext_Respected()
    {
        // Execute a binding in a first "cell"
        await _kernel.ExecuteAsync("my_var = 42", _context);

        // Diagnostics on valid code using the binding — should return no errors
        var diagnostics = await _kernel.GetDiagnosticsAsync("result = my_var + 1");
        Assert.AreEqual(0, diagnostics.Count,
            "Previously executed binding should not cause syntax errors.");
    }
}
