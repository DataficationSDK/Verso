using Verso.Abstractions;
using Verso.PowerShell.Kernel;
using Verso.Testing.Stubs;

namespace Verso.PowerShell.Tests.VariableSharing;

[TestClass]
public class ConsumeTests
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
    public async Task VersoVariablesGet_RetrievesFromStore()
    {
        _context.Variables.Set("externalValue", 42);

        var outputs = await _kernel.ExecuteAsync(
            "$VersoVariables.Get[int]('externalValue')", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("42"), $"Expected '42', got: {allText}");
    }

    [TestMethod]
    public async Task InjectedVariable_AccessibleDirectly()
    {
        _context.Variables.Set("fromOtherKernel", "shared data");

        var outputs = await _kernel.ExecuteAsync(
            "Write-Output $fromOtherKernel", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("shared data"), $"Expected 'shared data', got: {allText}");
    }

    [TestMethod]
    public async Task CrossKernel_VariableSharing()
    {
        // Simulate another kernel setting a variable
        _context.Variables.Set("fromCSharp", "shared data");

        // PowerShell kernel reads it
        var outputs = await _kernel.ExecuteAsync(
            "Write-Output $fromCSharp", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("shared data"), $"Expected 'shared data', got: {allText}");

        // PowerShell kernel writes a variable
        await _kernel.ExecuteAsync("$fromPowerShell = 123", _context);
        Assert.IsTrue(_context.Variables.TryGet<int>("fromPowerShell", out var val));
        Assert.AreEqual(123, val);
    }

    [TestMethod]
    public async Task InjectedVariable_UpdatedBetweenCells()
    {
        _context.Variables.Set("counter", 1);
        await _kernel.ExecuteAsync("Write-Output $counter", _context);

        // Simulate another kernel updating the variable
        _context.Variables.Set("counter", 2);
        var outputs = await _kernel.ExecuteAsync("Write-Output $counter", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("2"), $"Expected '2', got: {allText}");
    }
}
