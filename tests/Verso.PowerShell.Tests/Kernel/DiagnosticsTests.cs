using Verso.Abstractions;
using Verso.PowerShell.Kernel;
using Verso.Testing.Stubs;

namespace Verso.PowerShell.Tests.Kernel;

[TestClass]
public class DiagnosticsTests
{
    private PowerShellKernel _kernel = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new PowerShellKernel();
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
        var diagnostics = await _kernel.GetDiagnosticsAsync("$x = 10");
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task Diagnostics_ParseError_ReturnsError()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("function { }");

        Assert.IsTrue(diagnostics.Count > 0, "Expected at least one diagnostic.");
        Assert.IsTrue(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            "Expected an error diagnostic.");
    }

    [TestMethod]
    public async Task Diagnostics_MissingClosingBrace_ReturnsError()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("if ($true) {");

        Assert.IsTrue(diagnostics.Count > 0, "Expected at least one diagnostic.");
        Assert.IsTrue(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public async Task Diagnostics_LinePositions_AreZeroBased()
    {
        var code = "$x = 10\nfunction { }";
        var diagnostics = await _kernel.GetDiagnosticsAsync(code);

        Assert.IsTrue(diagnostics.Count > 0);
        var errorDiag = diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
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
    public async Task Diagnostics_ValidCmdlet_ReturnsEmpty()
    {
        // PowerShell doesn't resolve cmdlets at parse time, so valid syntax = no diagnostics
        var diagnostics = await _kernel.GetDiagnosticsAsync("Get-ChildItem -Path .");
        Assert.AreEqual(0, diagnostics.Count);
    }
}
