using Verso.Abstractions;
using Verso.PowerShell.Kernel;
using Verso.Testing.Stubs;

namespace Verso.PowerShell.Tests.Kernel;

[TestClass]
public class CompletionTests
{
    private PowerShellKernel _kernel = null!;
    private StubExecutionContext _context = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new PowerShellKernel();
        await _kernel.InitializeAsync();
        _context = new StubExecutionContext();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task Completions_Variable_ReturnsVariableCompletions()
    {
        await _kernel.ExecuteAsync("$myTestVariable = 42", _context);

        var code = "$myTest";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0, "Expected completions for variable prefix.");
        Assert.IsTrue(completions.Any(c => c.InsertText.Contains("myTestVariable")),
            "Expected '$myTestVariable' in completions.");
    }

    [TestMethod]
    public async Task Completions_Cmdlet_ReturnsCmdletCompletions()
    {
        var code = "Get-Chi";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        Assert.IsTrue(completions.Count > 0, "Expected completions for cmdlet prefix.");
        Assert.IsTrue(completions.Any(c => c.DisplayText.Contains("Get-ChildItem")),
            "Expected 'Get-ChildItem' in completions.");
    }

    [TestMethod]
    public async Task Completions_EmptyCode_DoesNotThrow()
    {
        var completions = await _kernel.GetCompletionsAsync("", 0);
        Assert.IsNotNull(completions);
    }

    [TestMethod]
    public async Task Completions_KindMapping_Variable()
    {
        await _kernel.ExecuteAsync("$completionTestVar = 1", _context);

        var code = "$completionTest";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        var match = completions.FirstOrDefault(c => c.InsertText.Contains("completionTestVar"));
        if (match is not null)
        {
            Assert.AreEqual("Variable", match.Kind);
        }
    }

    [TestMethod]
    public async Task Completions_KindMapping_Command()
    {
        var code = "Get-Chi";
        var completions = await _kernel.GetCompletionsAsync(code, code.Length);

        var match = completions.FirstOrDefault(c => c.DisplayText.Contains("Get-ChildItem"));
        if (match is not null)
        {
            Assert.AreEqual("Method", match.Kind);
        }
    }
}
