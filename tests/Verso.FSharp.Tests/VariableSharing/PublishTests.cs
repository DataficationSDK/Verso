using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.FSharp.Tests.Helpers;

namespace Verso.FSharp.Tests.VariableSharing;

[TestClass]
public class PublishTests
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
    public async Task PrimitiveBinding_PublishedToStore()
    {
        await _kernel.ExecuteAsync("let myNumber = 42", _context);
        Assert.IsTrue(_context.Variables.TryGet<int>("myNumber", out var value));
        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public async Task StringBinding_PublishedToStore()
    {
        await _kernel.ExecuteAsync("let greeting = \"hello\"", _context);
        Assert.IsTrue(_context.Variables.TryGet<string>("greeting", out var value));
        Assert.AreEqual("hello", value);
    }

    [TestMethod]
    public async Task MultipleBindings_AllPublished()
    {
        await _kernel.ExecuteAsync("let a = 1\nlet b = 2\nlet c = 3", _context);
        Assert.IsTrue(_context.Variables.TryGet<int>("a", out var a));
        Assert.IsTrue(_context.Variables.TryGet<int>("b", out var b));
        Assert.IsTrue(_context.Variables.TryGet<int>("c", out var c));
        Assert.AreEqual(1, a);
        Assert.AreEqual(2, b);
        Assert.AreEqual(3, c);
    }

    [TestMethod]
    public async Task FunctionBinding_ExcludedFromStore()
    {
        await _kernel.ExecuteAsync("let myFunc x = x + 1", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsFalse(allVars.Any(v => v.Name == "myFunc"),
            "Function bindings should not be published to the variable store");
    }

    [TestMethod]
    public async Task UnderscorePrefixed_ExcludedFromStore()
    {
        await _kernel.ExecuteAsync("let _private = 42", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsFalse(allVars.Any(v => v.Name == "_private"),
            "Underscore-prefixed bindings should not be published");
    }

    [TestMethod]
    public async Task UnitBinding_ExcludedFromStore()
    {
        await _kernel.ExecuteAsync("let result = printfn \"test\"", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsFalse(allVars.Any(v => v.Name == "result"),
            "Unit-valued bindings should not be published");
    }

    [TestMethod]
    public async Task RecordBinding_PublishedToStore()
    {
        await _kernel.ExecuteAsync("type Point = { X: int; Y: int }\nlet pt = { X = 1; Y = 2 }", _context);
        Assert.IsTrue(_context.Variables.TryGet<object>("pt", out var value));
        Assert.IsNotNull(value);
    }

    [TestMethod]
    public async Task OverwriteBinding_UpdatesStore()
    {
        await _kernel.ExecuteAsync("let value = 1", _context);
        Assert.IsTrue(_context.Variables.TryGet<int>("value", out var first));
        Assert.AreEqual(1, first);

        // F# doesn't allow rebinding with let in FSI without shadowing,
        // but we can use a new cell with the same name
        await _kernel.ExecuteAsync("let value = 99", _context);
        Assert.IsTrue(_context.Variables.TryGet<int>("value", out var second));
        Assert.AreEqual(99, second);
    }

    [TestMethod]
    public async Task VersoHelpersModule_NotPublishedToStore()
    {
        // Execute something to trigger variable publishing
        await _kernel.ExecuteAsync("let x = 1", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsFalse(allVars.Any(v => v.Name == "VersoHelpers"),
            "VersoHelpers module should not be published to the variable store");
        Assert.IsFalse(allVars.Any(v => v.Name == "Variables"),
            "Variables binding should not be published to the variable store");
    }
}
