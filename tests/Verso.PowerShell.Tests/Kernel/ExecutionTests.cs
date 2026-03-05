using Verso.Abstractions;
using Verso.PowerShell.Kernel;
using Verso.Testing.Stubs;

namespace Verso.PowerShell.Tests.Kernel;

[TestClass]
public class ExecutionTests
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
    public async Task WriteOutput_ReturnsResult()
    {
        var outputs = await _kernel.ExecuteAsync("Write-Output 'hello'", _context);
        Assert.IsTrue(outputs.Count > 0);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("hello"), $"Expected 'hello' in output, got: {allText}");
    }

    [TestMethod]
    public async Task SimpleExpression_ReturnsResult()
    {
        var outputs = await _kernel.ExecuteAsync("1 + 2", _context);
        Assert.IsTrue(outputs.Count > 0);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("3"), $"Expected '3' in output, got: {allText}");
    }

    [TestMethod]
    public async Task WriteHost_CapturesInformationStream()
    {
        var outputs = await _kernel.ExecuteAsync("Write-Host 'hello world'", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("hello world"), $"Expected 'hello world', got: {allText}");
    }

    [TestMethod]
    public async Task WriteError_ReturnsErrorOutput()
    {
        var outputs = await _kernel.ExecuteAsync("Write-Error 'something failed'", _context);
        Assert.IsTrue(outputs.Any(o => o.IsError), "Expected an error output");
        var errorOutput = outputs.First(o => o.IsError);
        Assert.IsTrue(errorOutput.Content.Contains("something failed"),
            $"Expected 'something failed' in error, got: {errorOutput.Content}");
        Assert.AreEqual("PSError", errorOutput.ErrorName);
    }

    [TestMethod]
    public async Task WriteWarning_ReturnsWarningPrefix()
    {
        var outputs = await _kernel.ExecuteAsync("Write-Warning 'caution'", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("[WARNING]") && allText.Contains("caution"),
            $"Expected '[WARNING] caution', got: {allText}");
    }

    [TestMethod]
    public async Task StateChaining_VariablePersistsAcrossCells()
    {
        await _kernel.ExecuteAsync("$x = 10", _context);
        var outputs = await _kernel.ExecuteAsync("$x * 2", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("20"), $"Expected '20', got: {allText}");
    }

    [TestMethod]
    public async Task FunctionDefinition_CanBeCalledLater()
    {
        await _kernel.ExecuteAsync("function Double($n) { $n * 2 }", _context);
        var outputs = await _kernel.ExecuteAsync("Double 21", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("42"), $"Expected '42', got: {allText}");
    }

    [TestMethod]
    public async Task Pipeline_Works()
    {
        var outputs = await _kernel.ExecuteAsync("1,2,3 | ForEach-Object { $_ * 2 }", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("2") && allText.Contains("4") && allText.Contains("6"),
            $"Expected mapped values, got: {allText}");
    }

    [TestMethod]
    public async Task RuntimeError_ReturnsErrorOutput()
    {
        var outputs = await _kernel.ExecuteAsync("throw 'test exception'", _context);
        Assert.IsTrue(outputs.Any(o => o.IsError), "Expected an error output");
        var errorOutput = outputs.First(o => o.IsError);
        Assert.IsTrue(errorOutput.Content.Contains("test exception"),
            $"Expected 'test exception' in error, got: {errorOutput.Content}");
    }

    [TestMethod]
    public async Task EmptyCode_ReturnsEmpty()
    {
        var outputs = await _kernel.ExecuteAsync("", _context);
        Assert.AreEqual(0, outputs.Count);
    }

    [TestMethod]
    public async Task WhitespaceCode_ReturnsEmpty()
    {
        var outputs = await _kernel.ExecuteAsync("   \n  ", _context);
        Assert.AreEqual(0, outputs.Count);
    }

    [TestMethod]
    public async Task ArrayExpression_FormatsOutput()
    {
        var outputs = await _kernel.ExecuteAsync("@(1, 2, 3)", _context);
        Assert.IsTrue(outputs.Count > 0);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("1") && allText.Contains("2") && allText.Contains("3"),
            $"Expected array elements in output, got: {allText}");
    }

    [TestMethod]
    public async Task HashtableExpression_Works()
    {
        var outputs = await _kernel.ExecuteAsync("@{ Name = 'Alice'; Age = 30 }", _context);
        Assert.IsTrue(outputs.Count > 0);
    }

    [TestMethod]
    public async Task StringInterpolation_Works()
    {
        var outputs = await _kernel.ExecuteAsync("$name = 'World'\nWrite-Output \"Hello, $name!\"", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("Hello, World!"), $"Expected 'Hello, World!', got: {allText}");
    }

    [TestMethod]
    public async Task MultipleOutputs_AllCaptured()
    {
        var outputs = await _kernel.ExecuteAsync(
            "Write-Output 'first'\nWrite-Output 'second'", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("first"), $"Expected 'first', got: {allText}");
        Assert.IsTrue(allText.Contains("second"), $"Expected 'second', got: {allText}");
    }
}
