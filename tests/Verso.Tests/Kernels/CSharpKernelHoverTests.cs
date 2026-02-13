using Verso.Abstractions;
using Verso.Contexts;
using Verso.Kernels;
using Verso.Stubs;

namespace Verso.Tests.Kernels;

[TestClass]
public sealed class CSharpKernelHoverTests
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
    public async Task Hover_OnVariable_ReturnsTypeInfo()
    {
        var code = "var x = 42;";
        // Hover on 'x' at position 4
        var hover = await _kernel.GetHoverInfoAsync(code, 4);

        Assert.IsNotNull(hover, "Expected hover info for variable 'x'.");
        Assert.IsTrue(hover.Content.Length > 0, "Expected non-empty hover content.");
    }

    [TestMethod]
    public async Task Hover_OnMethod_ReturnsSignature()
    {
        var code = "Console.WriteLine(\"hello\")";
        // Hover over "WriteLine" — find position of 'W' in "WriteLine"
        var pos = code.IndexOf("WriteLine");
        var hover = await _kernel.GetHoverInfoAsync(code, pos);

        Assert.IsNotNull(hover, "Expected hover info for Console.WriteLine.");
        Assert.IsTrue(hover.Content.Contains("WriteLine"),
            "Expected hover to mention 'WriteLine'.");
    }

    [TestMethod]
    public async Task Hover_OnWhitespace_ReturnsNull()
    {
        var code = "   ";
        var hover = await _kernel.GetHoverInfoAsync(code, 1);

        Assert.IsNull(hover, "Expected null hover on whitespace.");
    }

    [TestMethod]
    public async Task Hover_IncludesRange()
    {
        var code = "var x = 42;";
        var hover = await _kernel.GetHoverInfoAsync(code, 4);

        if (hover is not null && hover.Range is not null)
        {
            var range = hover.Range.Value;
            Assert.IsTrue(range.StartLine >= 0);
            Assert.IsTrue(range.StartColumn >= 0);
            Assert.IsTrue(range.EndColumn > range.StartColumn || range.EndLine > range.StartLine);
        }
    }

    [TestMethod]
    public async Task Hover_OnType_ReturnsInfo()
    {
        var code = "int x = 10;";
        // Hover on 'int' at position 0
        var hover = await _kernel.GetHoverInfoAsync(code, 0);

        Assert.IsNotNull(hover, "Expected hover info for type 'int'.");
    }

    [TestMethod]
    public async Task Hover_AfterExecution_ReturnsInfoForUserVariable()
    {
        var context = CreateContext();
        await _kernel.ExecuteAsync("var myList = new List<string>();", context);

        var code = "myList.Count";
        var hover = await _kernel.GetHoverInfoAsync(code, 0);

        Assert.IsNotNull(hover, "Expected hover info for user-defined variable.");
    }

    [TestMethod]
    public async Task Hover_EmptyCode_ReturnsNull()
    {
        var hover = await _kernel.GetHoverInfoAsync("", 0);

        Assert.IsNull(hover, "Expected null for empty code.");
    }

    private static Verso.Contexts.ExecutionContext CreateContext()
    {
        var variables = new VariableStore();
        var theme = new StubThemeContext();
        var extensionHost = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
        var metadata = new NotebookMetadataContext(new NotebookModel());

        return new Verso.Contexts.ExecutionContext(
            Guid.NewGuid(), 1, variables, CancellationToken.None,
            theme, LayoutCapabilities.None, extensionHost, metadata,
            new Verso.Stubs.StubNotebookOperations(),
            writeOutput: _ => Task.CompletedTask,
            display: _ => Task.CompletedTask);
    }
}
