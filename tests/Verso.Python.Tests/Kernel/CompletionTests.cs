using Verso.Abstractions;
using Verso.Python.Kernel;
using Verso.Python.Tests.Host;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Kernel;

[TestClass]
public sealed class CompletionTests
{
    private PythonKernel _kernel = null!;
    private StubExecutionContext _context = null!;
    private bool _jediAvailable;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new PythonKernel();
        await _kernel.InitializeAsync();
        _context = new StubExecutionContext();

        // The analysis library is installed in the background as the kernel starts, so probing
        // for it straight away would race that. Settled first, and left alone on a machine that
        // cannot install it, where the tests needing it report inconclusive.
        if (EmbeddedRuntimeOnly.HostProcessEnabled)
            await AnalysisTools.TryEnsureAsync();

        // Hover is the discriminator: it is the one answer the standard-library fallbacks cannot
        // produce. Without the library, completions come from the standard completer, which
        // reports full dotted names ("os.path") where the library reports the segment ("path").
        _jediAvailable = await _kernel.GetHoverInfoAsync("len", 1) is not null;
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    private void RequireJedi()
    {
        if (!_jediAvailable)
            Assert.Inconclusive("jedi is not available in this environment.");
    }

    /// <summary>
    /// What came back, for a failure message. Which component answered is readable from the
    /// shape: the analysis library reports the trailing segment of a name and the
    /// standard-library fallback reports the whole dotted path, so a failure that lists "x.append"
    /// is a fallback and one that lists nothing is a request that went unanswered.
    /// </summary>
    private static string Describe(IReadOnlyList<Completion> completions)
        => completions.Count == 0
            ? "nothing"
            : string.Join(", ", completions.Take(8).Select(c => c.DisplayText));

    [TestMethod]
    public async Task Completions_DotOnModule_ReturnsModuleMembers()
    {
        HostRuntimeOnly.Require("Editor assistance");

        RequireJedi();

        var code = "os.";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0, "Expected completions for os module.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "path"),
            "Expected 'path' in os completions.");
    }

    [TestMethod]
    public async Task Completions_AfterExecution_IncludesUserVariables()
    {
        HostRuntimeOnly.Require("Editor assistance");

        RequireJedi();

        await _kernel.ExecuteAsync("x = [1, 2, 3]", _context);

        var code = "x.";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0,
            "Expected completions for user variable 'x'.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "append"),
            "Expected 'append' in list completions. Got: " + Describe(completions));
    }

    [TestMethod]
    public async Task Completions_PartialTyping_FiltersByPrefix()
    {
        HostRuntimeOnly.Require("Editor assistance");

        RequireJedi();

        var code = "os.pa";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "path"),
            "Expected 'path' when typing 'os.pa'.");
    }

    [TestMethod]
    public async Task Completions_BuiltinAvailable()
    {
        HostRuntimeOnly.Require("Editor assistance");

        var code = "le";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "len"),
            "Expected 'len' in completions.");
    }

    [TestMethod]
    public async Task Completions_EmptyCode_DoesNotThrow()
    {
        HostRuntimeOnly.Require("Editor assistance");

        var completions = await _kernel.GetCompletionsAsync("", 0);
        Assert.IsNotNull(completions);
    }

    [TestMethod]
    public async Task Completions_KindMapping_ReturnsCorrectKinds()
    {
        HostRuntimeOnly.Require("Editor assistance");

        var code = "os.";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0, "Expected completions for os module.");
        Assert.IsTrue(completions.All(c => !string.IsNullOrEmpty(c.Kind)),
            "All completions should have a Kind.");
    }
}
