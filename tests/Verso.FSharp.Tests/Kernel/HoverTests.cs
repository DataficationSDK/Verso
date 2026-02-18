using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;

[TestClass]
public class HoverTests
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
    public async Task Hover_LetBinding_ShowsTypeInfo()
    {
        var code = "let x = 42";
        // Hover on 'x' at position 4
        var hover = await _kernel.GetHoverInfoAsync(code, 4);

        Assert.IsNotNull(hover, "Expected hover info for let binding 'x'.");
        Assert.IsTrue(hover.Content.Length > 0, "Expected non-empty hover content.");
        Assert.IsTrue(hover.Content.Contains("int") || hover.Content.Contains("Int32"),
            $"Expected type info containing 'int', got: {hover.Content}");
    }

    [TestMethod]
    public async Task Hover_Function_ShowsSignature()
    {
        var code = "let add a b = a + b";
        // Hover on 'add'
        var pos = code.IndexOf("add");
        var hover = await _kernel.GetHoverInfoAsync(code, pos);

        Assert.IsNotNull(hover, "Expected hover info for function 'add'.");
        Assert.IsTrue(hover.Content.Contains("add") || hover.Content.Contains("->"),
            $"Expected function signature info, got: {hover.Content}");
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
        var code = "let x = 42";
        var hover = await _kernel.GetHoverInfoAsync(code, 4);

        if (hover is not null && hover.Range is not null)
        {
            var range = hover.Range.Value;
            Assert.IsTrue(range.StartLine >= 0);
            Assert.IsTrue(range.StartColumn >= 0);
            Assert.IsTrue(range.EndColumn > range.StartColumn || range.EndLine > range.StartLine,
                "Range should have positive extent.");
        }
    }

    [TestMethod]
    public async Task Hover_AfterExecution_ReturnsInfoForUserBinding()
    {
        await _kernel.ExecuteAsync("let myList = [1; 2; 3]", _context);

        var code = "myList.Length";
        var hover = await _kernel.GetHoverInfoAsync(code, 0);

        Assert.IsNotNull(hover, "Expected hover info for user-defined binding.");
    }
}
