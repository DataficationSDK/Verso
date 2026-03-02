using Verso.Python.Kernel;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Kernel;

[TestClass]
public sealed class CompletionTests
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
    public async Task Completions_DotOnModule_ReturnsModuleMembers()
    {
        var code = "os.";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0, "Expected completions for os module.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "path"),
            "Expected 'path' in os completions.");
    }

    [TestMethod]
    public async Task Completions_AfterExecution_IncludesUserVariables()
    {
        await _kernel.ExecuteAsync("x = [1, 2, 3]", _context);

        var code = "x.";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0,
            "Expected completions for user variable 'x'.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "append"),
            "Expected 'append' in list completions.");
    }

    [TestMethod]
    public async Task Completions_PartialTyping_FiltersByPrefix()
    {
        var code = "os.pa";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "path"),
            "Expected 'path' when typing 'os.pa'.");
    }

    [TestMethod]
    public async Task Completions_BuiltinAvailable()
    {
        var code = "le";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "len"),
            "Expected 'len' in completions.");
    }

    [TestMethod]
    public async Task Completions_EmptyCode_DoesNotThrow()
    {
        var completions = await _kernel.GetCompletionsAsync("", 0);
        Assert.IsNotNull(completions);
    }

    [TestMethod]
    public async Task Completions_KindMapping_ReturnsCorrectKinds()
    {
        var code = "os.";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0, "Expected completions for os module.");
        // All completions should have a non-null Kind
        Assert.IsTrue(completions.All(c => !string.IsNullOrEmpty(c.Kind)),
            "All completions should have a Kind.");
    }
}
