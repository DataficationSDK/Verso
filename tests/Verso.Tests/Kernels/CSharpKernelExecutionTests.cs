using Verso.Abstractions;
using Verso.Contexts;
using Verso.Kernels;
using Verso.Stubs;

namespace Verso.Tests.Kernels;

[TestClass]
public sealed class CSharpKernelExecutionTests
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
    public async Task Execute_Expression_ReturnsValue()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync("1 + 2", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("3", outputs[0].Content);
        Assert.IsFalse(outputs[0].IsError);
    }

    [TestMethod]
    public async Task Execute_ConsoleWrite_CapturesOutput()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync("Console.Write(\"hello\")", context);

        Assert.IsTrue(outputs.Count >= 1);
        Assert.IsTrue(outputs.Any(o => o.Content.Contains("hello")));
    }

    [TestMethod]
    public async Task Execute_ConsoleWriteLine_CapturesOutput()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync("Console.WriteLine(\"world\")", context);

        Assert.IsTrue(outputs.Count >= 1);
        Assert.IsTrue(outputs.Any(o => o.Content.Contains("world")));
    }

    [TestMethod]
    public async Task Execute_VariablePersistence_AcrossCells()
    {
        var context = CreateContext();
        await _kernel.ExecuteAsync("var x = 10;", context);

        var outputs = await _kernel.ExecuteAsync("x * 2", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("20", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_MethodDefinition_PersistsAcrossCells()
    {
        var context = CreateContext();
        await _kernel.ExecuteAsync("int Square(int n) => n * n;", context);

        var outputs = await _kernel.ExecuteAsync("Square(5)", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("25", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_ClassDefinition_PersistsAcrossCells()
    {
        var context = CreateContext();
        await _kernel.ExecuteAsync("class Greeter { public string Greet(string name) => $\"Hello, {name}!\"; }", context);

        var outputs = await _kernel.ExecuteAsync("new Greeter().Greet(\"Verso\")", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("Hello, Verso!", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_CompilationError_ReturnsErrorOutput()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync("int x = \"not an int\";", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].IsError);
        Assert.AreEqual("CompilationError", outputs[0].ErrorName);
    }

    [TestMethod]
    public async Task Execute_RuntimeError_ReturnsErrorOutput()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync("throw new InvalidOperationException(\"boom\");", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].IsError);
        Assert.IsTrue(outputs[0].Content.Contains("boom"));
    }

    [TestMethod]
    public async Task Execute_EmptyCode_ReturnsEmptyList()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync("", context);

        Assert.AreEqual(0, outputs.Count);
    }

    [TestMethod]
    public async Task Execute_WhitespaceCode_ReturnsEmptyList()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync("   \n  \t  ", context);

        Assert.AreEqual(0, outputs.Count);
    }

    [TestMethod]
    public async Task Execute_AsyncCode_Works()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync("await Task.FromResult(42)", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("42", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_LinqQuery_Works()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync(
            "new[] { 1, 2, 3, 4, 5 }.Where(x => x > 2).Sum()", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("12", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_PublishesVariables_ToContext()
    {
        var context = CreateContext();
        await _kernel.ExecuteAsync("var myVar = 42;", context);

        Assert.IsTrue(context.Variables.TryGet<int>("myVar", out var value));
        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public async Task Execute_VoidStatement_ReturnsNoReturnOutput()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync("var x = 10;", context);

        // A statement that assigns a variable returns null as return value
        // so there should be no outputs (no console output, no return value)
        Assert.AreEqual(0, outputs.Count);
    }

    [TestMethod]
    public async Task Execute_StringExpression_ReturnsValue()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync("\"hello world\"", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("hello world", outputs[0].Content);
    }

    [TestMethod]
    public async Task Execute_MultipleConsoleOutputs_Combined()
    {
        var context = CreateContext();
        var outputs = await _kernel.ExecuteAsync(
            "Console.Write(\"a\"); Console.Write(\"b\"); Console.Write(\"c\");", context);

        Assert.IsTrue(outputs.Any(o => o.Content.Contains("abc")));
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
            writeOutput: _ => Task.CompletedTask,
            display: _ => Task.CompletedTask);
    }
}
