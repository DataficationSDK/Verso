using Verso.Abstractions;
using Verso.PowerShell.Kernel;
using Verso.Testing.Stubs;

namespace Verso.PowerShell.Tests.Kernel;

[TestClass]
public class HoverTests
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
    public async Task Hover_OnCmdlet_ShowsCommandInfo()
    {
        var code = "Get-ChildItem";
        var hover = await _kernel.GetHoverInfoAsync(code, 0);

        Assert.IsNotNull(hover, "Expected hover info for cmdlet.");
        Assert.IsTrue(hover.Content.Length > 0, "Expected non-empty hover content.");
        Assert.IsTrue(hover.Content.Contains("Command") || hover.Content.Contains("Get-ChildItem"),
            $"Expected command info, got: {hover.Content}");
    }

    [TestMethod]
    public async Task Hover_OnVariable_ShowsVariableInfo()
    {
        var code = "$myVar";
        var hover = await _kernel.GetHoverInfoAsync(code, 1);

        Assert.IsNotNull(hover, "Expected hover info for variable.");
        Assert.IsTrue(hover.Content.Contains("Variable") || hover.Content.Contains("myVar"),
            $"Expected variable info, got: {hover.Content}");
    }

    [TestMethod]
    public async Task Hover_OnWhitespace_ReturnsNull()
    {
        var code = "   ";
        var hover = await _kernel.GetHoverInfoAsync(code, 1);

        Assert.IsNull(hover, "Expected null hover on whitespace.");
    }

    [TestMethod]
    public async Task Hover_EmptyCode_ReturnsNull()
    {
        var hover = await _kernel.GetHoverInfoAsync("", 0);
        Assert.IsNull(hover, "Expected null for empty code.");
    }

    [TestMethod]
    public async Task Hover_IncludesRange()
    {
        var code = "Get-ChildItem";
        var hover = await _kernel.GetHoverInfoAsync(code, 0);

        if (hover is not null && hover.Range is not null)
        {
            var range = hover.Range.Value;
            Assert.IsTrue(range.StartLine >= 0);
            Assert.IsTrue(range.StartColumn >= 0);
            Assert.IsTrue(range.EndColumn > range.StartColumn || range.EndLine > range.StartLine,
                "Range should have positive extent.");
        }
    }
}
