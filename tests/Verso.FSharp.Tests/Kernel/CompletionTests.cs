using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.FSharp.Tests.Helpers;

namespace Verso.FSharp.Tests.Kernel;

[TestClass]
public class CompletionTests
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
    public async Task Completions_DotOnString_ReturnsStringMembers()
    {
        var code = "\"hello\".";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0, "Expected completions for string.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "Length"),
            "Expected 'Length' in string completions.");
    }

    [TestMethod]
    public async Task Completions_AfterExecution_IncludesUserBindings()
    {
        await _kernel.ExecuteAsync("let items = [1; 2; 3]", _context);

        var code = "items.";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0,
            "Expected completions for user binding 'items'.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "Length"),
            "Expected 'Length' in list completions.");
    }

    [TestMethod]
    public async Task Completions_PartialTyping_FiltersByPrefix()
    {
        var code = "\"hello\".Len";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "Length"),
            "Expected 'Length' when typing 'Len'.");
    }

    [TestMethod]
    public async Task Completions_SystemNamespace_Available()
    {
        var code = "Console.";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0, "Expected Console completions.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "WriteLine"),
            "Expected 'WriteLine' in Console completions.");
    }

    [TestMethod]
    public async Task Completions_EmptyCode_DoesNotThrow()
    {
        var completions = await _kernel.GetCompletionsAsync("", 0);
        Assert.IsNotNull(completions);
    }

    [TestMethod]
    public async Task Completions_KindMapping_Property()
    {
        var code = "\"hello\".";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        var lengthCompletion = completions.FirstOrDefault(c => c.DisplayText == "Length");
        Assert.IsNotNull(lengthCompletion, "Expected 'Length' in string completions.");
        Assert.AreEqual("Property", lengthCompletion.Kind);
    }

    [TestMethod]
    public async Task Completions_KindMapping_Method()
    {
        var code = "\"hello\".";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        var containsCompletion = completions.FirstOrDefault(c => c.DisplayText == "Contains");
        Assert.IsNotNull(containsCompletion, "Expected 'Contains' in string completions.");
        Assert.AreEqual("Method", containsCompletion.Kind);
    }
}
