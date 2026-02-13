using Verso.Abstractions;
using Verso.Contexts;
using Verso.Kernels;
using Verso.Stubs;

namespace Verso.Tests.Kernels;

[TestClass]
public sealed class CSharpKernelCompletionTests
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
    public async Task Completions_DotOnString_ReturnsStringMembers()
    {
        var code = "\"hello\".";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0, "Expected completions for string.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "Length"),
            "Expected 'Length' in string completions.");
    }

    [TestMethod]
    public async Task Completions_DotOnList_ReturnsListMembers()
    {
        var code = "new List<int>().";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0);
        Assert.IsTrue(completions.Any(c => c.DisplayText == "Add"),
            "Expected 'Add' in List<int> completions.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "Count"),
            "Expected 'Count' in List<int> completions.");
    }

    [TestMethod]
    public async Task Completions_AfterExecution_IncludesUserVariables()
    {
        var context = CreateContext();
        await _kernel.ExecuteAsync("var items = new List<int>();", context);

        var code = "items.";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0);
        Assert.IsTrue(completions.Any(c => c.DisplayText == "Add"),
            "Expected 'Add' in completions for user variable 'items'.");
    }

    [TestMethod]
    public async Task Completions_Filtering_PartialTyping()
    {
        var code = "\"hello\".Len";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "Length"),
            "Expected 'Length' when typing 'Len'.");
    }

    [TestMethod]
    public async Task Completions_EmptyCode_ReturnsResults()
    {
        // At an empty position, Roslyn should still offer top-level completions
        var completions = await _kernel.GetCompletionsAsync("", 0);

        // Could be empty or contain keywords/types — just verify no exception
        Assert.IsNotNull(completions);
    }

    [TestMethod]
    public async Task Completions_KindMapping_Methods()
    {
        var code = "\"hello\".";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        var containsCompletion = completions.FirstOrDefault(c => c.DisplayText == "Contains");
        Assert.IsNotNull(containsCompletion, "Expected 'Contains' in string completions.");
        Assert.AreEqual("Method", containsCompletion.Kind);
    }

    [TestMethod]
    public async Task Completions_KindMapping_Properties()
    {
        var code = "\"hello\".";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        var lengthCompletion = completions.FirstOrDefault(c => c.DisplayText == "Length");
        Assert.IsNotNull(lengthCompletion, "Expected 'Length' in string completions.");
        Assert.AreEqual("Property", lengthCompletion.Kind);
    }

    [TestMethod]
    public async Task Completions_SystemNamespace_Available()
    {
        var code = "Console.";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0);
        Assert.IsTrue(completions.Any(c => c.DisplayText == "WriteLine"),
            "Expected 'WriteLine' in Console completions.");
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
