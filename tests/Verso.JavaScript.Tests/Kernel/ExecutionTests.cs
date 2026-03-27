using Verso.Abstractions;
using Verso.JavaScript.Kernel;
using Verso.Testing.Stubs;

namespace Verso.JavaScript.Tests.Kernel;

[TestClass]
public class ExecutionTests
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
    public async Task ConsoleLog_ReturnsTextOutput()
    {
        var outputs = await _kernel.ExecuteAsync("console.log('hello')", _context);
        Assert.IsTrue(outputs.Count > 0);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("hello"), $"Expected 'hello' in output, got: {allText}");
    }

    [TestMethod]
    public async Task SimpleExpression_ReturnsJsonOutput()
    {
        var outputs = await _kernel.ExecuteAsync("1 + 2", _context);
        Assert.IsTrue(outputs.Count > 0);
        var jsonOutput = outputs.FirstOrDefault(o => o.MimeType == "application/json");
        Assert.IsNotNull(jsonOutput, "Expected a JSON output for expression result");
        Assert.IsTrue(jsonOutput.Content.Contains("3"), $"Expected '3' in output, got: {jsonOutput.Content}");
    }

    [TestMethod]
    public async Task ConsoleError_ReturnsErrorOutput()
    {
        var outputs = await _kernel.ExecuteAsync("console.error('something failed')", _context);
        Assert.IsTrue(outputs.Any(o => o.IsError), "Expected an error output");
        var errorOutput = outputs.First(o => o.IsError);
        Assert.IsTrue(errorOutput.Content.Contains("something failed"),
            $"Expected 'something failed' in error, got: {errorOutput.Content}");
    }

    [TestMethod]
    public async Task RuntimeError_ReturnsErrorOutput()
    {
        var outputs = await _kernel.ExecuteAsync("throw new Error('test exception')", _context);
        Assert.IsTrue(outputs.Any(o => o.IsError), "Expected an error output");
        var errorOutput = outputs.First(o => o.IsError);
        Assert.IsTrue(errorOutput.Content.Contains("test exception"),
            $"Expected 'test exception' in error, got: {errorOutput.Content}");
        Assert.AreEqual("JavaScriptError", errorOutput.ErrorName);
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
    public async Task StateChaining_VariablePersistsAcrossCells()
    {
        await _kernel.ExecuteAsync("var x = 10", _context);
        var outputs = await _kernel.ExecuteAsync("x * 2", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("20"), $"Expected '20', got: {allText}");
    }

    [TestMethod]
    public async Task FunctionDefinition_CanBeCalledLater()
    {
        await _kernel.ExecuteAsync("function double(n) { return n * 2; }", _context);
        var outputs = await _kernel.ExecuteAsync("double(21)", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("42"), $"Expected '42', got: {allText}");
    }

    [TestMethod]
    public async Task ConsoleLogMultiple_AllCaptured()
    {
        var outputs = await _kernel.ExecuteAsync(
            "console.log('first')\nconsole.log('second')", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("first"), $"Expected 'first', got: {allText}");
        Assert.IsTrue(allText.Contains("second"), $"Expected 'second', got: {allText}");
    }

    [TestMethod]
    public async Task ObjectExpression_ReturnsJson()
    {
        var outputs = await _kernel.ExecuteAsync("({name: 'Alice', age: 30})", _context);
        var jsonOutput = outputs.FirstOrDefault(o => o.MimeType == "application/json");
        Assert.IsNotNull(jsonOutput, "Expected JSON output for object expression");
        Assert.IsTrue(jsonOutput.Content.Contains("Alice"), $"Expected 'Alice' in output, got: {jsonOutput.Content}");
    }

    [TestMethod]
    public async Task ArrayExpression_ReturnsJson()
    {
        var outputs = await _kernel.ExecuteAsync("[1, 2, 3]", _context);
        var jsonOutput = outputs.FirstOrDefault(o => o.MimeType == "application/json");
        Assert.IsNotNull(jsonOutput, "Expected JSON output for array expression");
    }

    [TestMethod]
    public async Task UndefinedResult_NoExpressionOutput()
    {
        var outputs = await _kernel.ExecuteAsync("var x = 5;", _context);
        var jsonOutput = outputs.FirstOrDefault(o => o.MimeType == "application/json");
        Assert.IsNull(jsonOutput, "Expected no JSON output for variable declaration");
    }

    [TestMethod]
    public async Task ConsoleWarn_GoesToStderr()
    {
        var outputs = await _kernel.ExecuteAsync("console.warn('caution')", _context);
        Assert.IsTrue(outputs.Any(o => o.IsError), "Expected console.warn to produce error output");
        var warnOutput = outputs.First(o => o.IsError);
        Assert.IsTrue(warnOutput.Content.Contains("caution"),
            $"Expected 'caution' in output, got: {warnOutput.Content}");
    }

    [TestMethod]
    public async Task ConstRedeclaration_WorksAcrossCells()
    {
        await _kernel.ExecuteAsync("const x = 1", _context);
        var outputs = await _kernel.ExecuteAsync("const x = 2", _context);
        Assert.IsFalse(outputs.Any(o => o.IsError),
            "Re-declaring const across cells should not error");
    }

    [TestMethod]
    public async Task LetRedeclaration_WorksAcrossCells()
    {
        await _kernel.ExecuteAsync("let y = 1", _context);
        var outputs = await _kernel.ExecuteAsync("let y = 2", _context);
        Assert.IsFalse(outputs.Any(o => o.IsError),
            "Re-declaring let across cells should not error");
    }

    [TestMethod]
    public async Task ConstVariable_AccessibleAcrossCells()
    {
        await _kernel.ExecuteAsync("const greeting = 'hello'", _context);
        var outputs = await _kernel.ExecuteAsync("greeting", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("hello"), $"Expected 'hello', got: {allText}");
    }

    [TestMethod]
    public async Task StringConcatenation_Works()
    {
        var outputs = await _kernel.ExecuteAsync("'Hello' + ' ' + 'World'", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("Hello World"), $"Expected 'Hello World', got: {allText}");
    }

    [TestMethod]
    public async Task TemplateLiteral_Works()
    {
        await _kernel.ExecuteAsync("var name = 'World'", _context);
        var outputs = await _kernel.ExecuteAsync("console.log(`Hello, ${name}!`)", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("Hello, World!"), $"Expected 'Hello, World!', got: {allText}");
    }

    [TestMethod]
    public async Task ReferenceError_ReturnsError()
    {
        var outputs = await _kernel.ExecuteAsync("nonExistentVariable.toString()", _context);
        Assert.IsTrue(outputs.Any(o => o.IsError), "Expected an error for undefined variable");
    }

    [TestMethod]
    public async Task MathOperations_Work()
    {
        var outputs = await _kernel.ExecuteAsync("Math.PI", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("3.14"), $"Expected PI value, got: {allText}");
    }
}
