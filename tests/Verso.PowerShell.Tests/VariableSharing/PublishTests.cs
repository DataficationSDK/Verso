using Verso.Abstractions;
using Verso.PowerShell.Kernel;
using Verso.Testing.Stubs;

namespace Verso.PowerShell.Tests.VariableSharing;

[TestClass]
public class PublishTests
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
    public async Task PrimitiveVariable_PublishedToStore()
    {
        await _kernel.ExecuteAsync("$myNumber = 42", _context);
        Assert.IsTrue(_context.Variables.TryGet<int>("myNumber", out var value));
        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public async Task StringVariable_PublishedToStore()
    {
        await _kernel.ExecuteAsync("$greeting = 'hello'", _context);
        Assert.IsTrue(_context.Variables.TryGet<string>("greeting", out var value));
        Assert.AreEqual("hello", value);
    }

    [TestMethod]
    public async Task MultipleVariables_AllPublished()
    {
        await _kernel.ExecuteAsync("$a = 1\n$b = 2\n$c = 3", _context);
        Assert.IsTrue(_context.Variables.TryGet<int>("a", out var a));
        Assert.IsTrue(_context.Variables.TryGet<int>("b", out var b));
        Assert.IsTrue(_context.Variables.TryGet<int>("c", out var c));
        Assert.AreEqual(1, a);
        Assert.AreEqual(2, b);
        Assert.AreEqual(3, c);
    }

    [TestMethod]
    public async Task AutomaticVariable_ExcludedFromStore()
    {
        // Execute something to trigger variable publishing
        await _kernel.ExecuteAsync("$x = 1", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsFalse(allVars.Any(v => v.Name == "true"),
            "Automatic variable '$true' should not be published");
        Assert.IsFalse(allVars.Any(v => v.Name == "false"),
            "Automatic variable '$false' should not be published");
        Assert.IsFalse(allVars.Any(v => v.Name == "null"),
            "Automatic variable '$null' should not be published");
    }

    [TestMethod]
    public async Task UnderscorePrefixed_ExcludedFromStore()
    {
        await _kernel.ExecuteAsync("$_private = 42", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsFalse(allVars.Any(v => v.Name == "_private"),
            "Underscore-prefixed variables should not be published");
    }

    [TestMethod]
    public async Task OverwriteVariable_UpdatesStore()
    {
        await _kernel.ExecuteAsync("$value = 1", _context);
        Assert.IsTrue(_context.Variables.TryGet<int>("value", out var first));
        Assert.AreEqual(1, first);

        await _kernel.ExecuteAsync("$value = 99", _context);
        Assert.IsTrue(_context.Variables.TryGet<int>("value", out var second));
        Assert.AreEqual(99, second);
    }

    [TestMethod]
    public async Task VersoVariables_NotPublishedToStore()
    {
        // Execute something to trigger variable publishing
        await _kernel.ExecuteAsync("$x = 1", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsFalse(allVars.Any(v => v.Name == "VersoVariables"),
            "VersoVariables should not be published to the variable store");
    }

    [TestMethod]
    public async Task HashtableVariable_ConvertedToDictionary()
    {
        await _kernel.ExecuteAsync("$ht = @{ Name = 'Alice'; Age = 30 }", _context);
        Assert.IsTrue(_context.Variables.TryGet<Dictionary<string, object>>("ht", out var dict));
        Assert.IsNotNull(dict);
        Assert.AreEqual("Alice", dict["Name"]);
    }
}
