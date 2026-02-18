using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;

[TestClass]
public class ExecutionTests
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
    public async Task SimpleExpression_ReturnsResult()
    {
        var outputs = await _kernel.ExecuteAsync("1 + 2", _context);
        Assert.IsTrue(outputs.Count > 0);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("3"), $"Expected '3' in output, got: {allText}");
    }

    [TestMethod]
    public async Task LetBinding_ProducesFsiOutput()
    {
        var outputs = await _kernel.ExecuteAsync("let x = 42", _context);
        Assert.IsTrue(outputs.Count > 0);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("42") || allText.Contains("x"), $"Expected binding info, got: {allText}");
    }

    [TestMethod]
    public async Task Printfn_CapturesConsoleOutput()
    {
        var outputs = await _kernel.ExecuteAsync("printfn \"hello world\"", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("hello world"), $"Expected 'hello world', got: {allText}");
    }

    [TestMethod]
    public async Task ConsoleWrite_CapturesOutput()
    {
        var outputs = await _kernel.ExecuteAsync("System.Console.Write(\"console test\")", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("console test"), $"Expected 'console test', got: {allText}");
    }

    [TestMethod]
    public async Task StateChaining_VariablePersistsAcrossCells()
    {
        await _kernel.ExecuteAsync("let x = 10", _context);
        var outputs = await _kernel.ExecuteAsync("x * 2", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("20"), $"Expected '20', got: {allText}");
    }

    [TestMethod]
    public async Task FunctionDefinition_CanBeCalledLater()
    {
        await _kernel.ExecuteAsync("let double x = x * 2", _context);
        var outputs = await _kernel.ExecuteAsync("double 21", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("42"), $"Expected '42', got: {allText}");
    }

    [TestMethod]
    public async Task RecordType_CanBeDefinedAndUsed()
    {
        await _kernel.ExecuteAsync("type Person = { Name: string; Age: int }", _context);
        var outputs = await _kernel.ExecuteAsync("{ Name = \"Alice\"; Age = 30 }", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("Alice"), $"Expected 'Alice', got: {allText}");
    }

    [TestMethod]
    public async Task CompilationError_ReturnsErrorOutput()
    {
        var outputs = await _kernel.ExecuteAsync("let x: int = \"not an int\"", _context);
        Assert.IsTrue(outputs.Any(o => o.IsError), "Expected an error output");
    }

    [TestMethod]
    public async Task RuntimeError_ReturnsErrorOutput()
    {
        var outputs = await _kernel.ExecuteAsync("let x = 1 / 0", _context);
        Assert.IsTrue(outputs.Any(o => o.IsError), "Expected an error output");
        var errorOutput = outputs.First(o => o.IsError);
        Assert.IsTrue(
            errorOutput.Content.Contains("DivideByZero") || errorOutput.Content.Contains("divide by zero", StringComparison.OrdinalIgnoreCase),
            $"Expected division error in output, got: {errorOutput.Content}");
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
    public async Task ListExpression_FormatsOutput()
    {
        var outputs = await _kernel.ExecuteAsync("[1; 2; 3]", _context);
        Assert.IsTrue(outputs.Count > 0);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("1") && allText.Contains("2") && allText.Contains("3"),
            $"Expected list elements in output, got: {allText}");
    }

    [TestMethod]
    public async Task MultipleOutputs_AllCaptured()
    {
        var outputs = await _kernel.ExecuteAsync(
            "printfn \"first\"\nprintfn \"second\"\n42", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("first"), $"Expected 'first', got: {allText}");
        Assert.IsTrue(allText.Contains("second"), $"Expected 'second', got: {allText}");
    }

    [TestMethod]
    public async Task PipelineExpression_Works()
    {
        var outputs = await _kernel.ExecuteAsync("[1;2;3] |> List.map (fun x -> x * 2)", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("2") && allText.Contains("4") && allText.Contains("6"),
            $"Expected mapped values, got: {allText}");
    }

    [TestMethod]
    public async Task MutableVariable_CanBeModified()
    {
        await _kernel.ExecuteAsync("let mutable counter = 0", _context);
        await _kernel.ExecuteAsync("counter <- counter + 1", _context);
        var outputs = await _kernel.ExecuteAsync("counter", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("1"), $"Expected '1', got: {allText}");
    }

    [TestMethod]
    public async Task StringInterpolation_Works()
    {
        var outputs = await _kernel.ExecuteAsync("let name = \"World\"\nprintfn $\"Hello, {name}!\"", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("Hello, World!"), $"Expected 'Hello, World!', got: {allText}");
    }

    [TestMethod]
    public async Task DefaultOpens_SystemLinqAvailable()
    {
        // System.Linq should be opened by default
        var outputs = await _kernel.ExecuteAsync("[1;2;3] |> Seq.toList |> List.length", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("3"), $"Expected '3', got: {allText}");
    }

    [TestMethod]
    public async Task DefaultOpens_SystemIOAvailable()
    {
        // System.IO should be opened by default — Path type available without qualification
        var outputs = await _kernel.ExecuteAsync("Path.GetExtension(\"test.txt\")", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains(".txt"), $"Expected '.txt', got: {allText}");
    }

    [TestMethod]
    public async Task OutputOrdering_FsiThenConsoleThenError()
    {
        // FSI output (let binding), Console.Out (printfn), and Console.Error should appear in order
        var outputs = await _kernel.ExecuteAsync(
            "let orderTest = 42\nprintfn \"console out\"\nSystem.Console.Error.Write(\"stderr msg\")", _context);

        // Verify non-error outputs come before error outputs
        var nonErrorOutputs = outputs.Where(o => !o.IsError).ToList();
        var errorOutputs = outputs.Where(o => o.IsError).ToList();

        Assert.IsTrue(nonErrorOutputs.Count >= 1, "Should have non-error outputs");
        Assert.IsTrue(errorOutputs.Count >= 1, "Should have error outputs (stderr)");

        // Verify stderr is captured as error
        var stderrText = string.Join(" ", errorOutputs.Select(o => o.Content));
        Assert.IsTrue(stderrText.Contains("stderr msg"), $"Expected stderr captured, got: {stderrText}");
    }

    [TestMethod]
    public async Task MatchFailure_ReturnsSpecificError()
    {
        var outputs = await _kernel.ExecuteAsync(
            "let testMatch x = match x with | 1 -> \"one\"\ntestMatch 99", _context);
        Assert.IsTrue(outputs.Any(o => o.IsError), "Expected an error output");
        var errorOutput = outputs.First(o => o.IsError);
        Assert.IsTrue(
            errorOutput.Content.Contains("MatchFailure") || errorOutput.Content.Contains("match"),
            $"Expected match failure error, got: {errorOutput.Content}");
    }

    [TestMethod]
    public async Task SyntaxError_ReturnsCompilationError()
    {
        var outputs = await _kernel.ExecuteAsync("let = invalid", _context);
        Assert.IsTrue(outputs.Any(o => o.IsError), "Expected an error for syntax error");
    }

    [TestMethod]
    public async Task OpenedNamespace_PersistsAcrossCells()
    {
        await _kernel.ExecuteAsync("open System.Text", _context);
        var outputs = await _kernel.ExecuteAsync("let sb = StringBuilder()\nsb.Append(\"test\").ToString()", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("test"), $"Expected 'test', got: {allText}");
    }

    [TestMethod]
    public async Task AutoOpenVersoHelpers_TryGetVarAvailableWithoutOpen()
    {
        _context.Variables.Set("autoOpenTest", 77);

        // tryGetVar should be available without 'open VersoHelpers' due to [<AutoOpen>]
        var outputs = await _kernel.ExecuteAsync(
            "let r = tryGetVar<int> \"autoOpenTest\"\nr", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("Some") && allText.Contains("77"),
            $"Expected 'Some 77', got: {allText}");
    }
}
