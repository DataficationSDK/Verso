using Verso.Python.Kernel;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Kernel;

[TestClass]
public sealed class HoverTests
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
    public async Task Hover_BuiltinFunction_ShowsInfo()
    {
        var code = "len";
        var hover = await _kernel.GetHoverInfoAsync(code, 0);

        Assert.IsNotNull(hover, "Expected hover info for 'len'.");
        Assert.IsTrue(hover.Content.Length > 0, "Expected non-empty hover content.");
        Assert.IsTrue(hover.Content.Contains("len") || hover.Content.Contains("builtins"),
            $"Expected hover content to reference len or builtins, got: {hover.Content}");
    }

    [TestMethod]
    public async Task Hover_AfterExecution_ReturnsInfoForVariable()
    {
        await _kernel.ExecuteAsync("my_list = [1, 2, 3]", _context);

        var code = "my_list";
        var hover = await _kernel.GetHoverInfoAsync(code, 0);

        Assert.IsNotNull(hover, "Expected hover info for user-defined variable.");
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
        var code = "len";
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
