using Verso.Abstractions;
using Verso.JavaScript.Kernel;
using Verso.Testing.Stubs;

namespace Verso.JavaScript.Tests.VariableSharing;

[TestClass]
public class ConsumeTests
{
    private JavaScriptKernel _kernel = null!;
    private StubExecutionContext _context = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new JavaScriptKernel(new JavaScriptKernelOptions { ForceJint = true });
        await _kernel.InitializeAsync();
        _context = new StubExecutionContext();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task InjectedVariable_AccessibleInCode()
    {
        _context.Variables.Set("fromOtherKernel", 42);

        var outputs = await _kernel.ExecuteAsync("fromOtherKernel", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("42"), $"Expected '42', got: {allText}");
    }

    [TestMethod]
    public async Task InjectedString_AccessibleInCode()
    {
        _context.Variables.Set("sharedText", "shared data");

        var outputs = await _kernel.ExecuteAsync("console.log(sharedText)", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("shared data"), $"Expected 'shared data', got: {allText}");
    }

    [TestMethod]
    public async Task CrossKernel_VariableSharing()
    {
        // Simulate another kernel setting a variable
        _context.Variables.Set("fromCSharp", "shared data");

        // JS kernel reads it
        var outputs = await _kernel.ExecuteAsync("console.log(fromCSharp)", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("shared data"), $"Expected 'shared data', got: {allText}");

        // JS kernel writes a variable
        await _kernel.ExecuteAsync("var fromJavaScript = 123", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsTrue(allVars.Any(v => v.Name == "fromJavaScript"),
            "Expected 'fromJavaScript' in variable store");
    }

    [TestMethod]
    public async Task InjectedVariable_UpdatedBetweenCells()
    {
        _context.Variables.Set("counter", 1);
        await _kernel.ExecuteAsync("console.log(counter)", _context);

        // Simulate another kernel updating the variable
        _context.Variables.Set("counter", 2);
        var outputs = await _kernel.ExecuteAsync("console.log(counter)", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("2"), $"Expected '2', got: {allText}");
    }

    [TestMethod]
    public async Task InjectVariablesDisabled_NothingInjected()
    {
        await _kernel.DisposeAsync();
        _kernel = new JavaScriptKernel(new JavaScriptKernelOptions
        {
            ForceJint = true,
            InjectVariables = false,
        });
        await _kernel.InitializeAsync();

        _context.Variables.Set("shouldNotBeAvailable", 42);
        var outputs = await _kernel.ExecuteAsync("typeof shouldNotBeAvailable", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("undefined"),
            $"Expected 'undefined' when InjectVariables is false, got: {allText}");
    }
}
