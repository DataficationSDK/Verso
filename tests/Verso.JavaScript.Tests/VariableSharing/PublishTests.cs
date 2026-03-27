using Verso.Abstractions;
using Verso.JavaScript.Kernel;
using Verso.Testing.Stubs;

namespace Verso.JavaScript.Tests.VariableSharing;

[TestClass]
public class PublishTests
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
    public async Task NumberVariable_PublishedToStore()
    {
        await _kernel.ExecuteAsync("var myNumber = 42", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsTrue(allVars.Any(v => v.Name == "myNumber"),
            "Expected 'myNumber' in variable store");
    }

    [TestMethod]
    public async Task StringVariable_PublishedToStore()
    {
        await _kernel.ExecuteAsync("var greeting = 'hello'", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsTrue(allVars.Any(v => v.Name == "greeting"),
            "Expected 'greeting' in variable store");
    }

    [TestMethod]
    public async Task MultipleVariables_AllPublished()
    {
        await _kernel.ExecuteAsync("var a = 1\nvar b = 2\nvar c = 3", _context);
        var allVars = _context.Variables.GetAll();
        var names = allVars.Select(v => v.Name).ToList();
        Assert.IsTrue(names.Contains("a"), "Expected 'a' in variable store");
        Assert.IsTrue(names.Contains("b"), "Expected 'b' in variable store");
        Assert.IsTrue(names.Contains("c"), "Expected 'c' in variable store");
    }

    [TestMethod]
    public async Task OverwriteVariable_UpdatesStore()
    {
        await _kernel.ExecuteAsync("var value = 1", _context);
        await _kernel.ExecuteAsync("var value = 99", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsTrue(allVars.Any(v => v.Name == "value"),
            "Expected 'value' in variable store after overwrite");
    }

    [TestMethod]
    public async Task VersoPrefixedVariable_NotPublished()
    {
        await _kernel.ExecuteAsync("var _versoCached = 42", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsFalse(allVars.Any(v => v.Name.StartsWith("_verso")),
            "Variables prefixed with _verso should not be published");
    }

    [TestMethod]
    public async Task ConstVariable_PublishedViaPromotion()
    {
        await _kernel.ExecuteAsync("const myConst = 'published'", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsTrue(allVars.Any(v => v.Name == "myConst"),
            "Expected const variable to be published via globalThis promotion");
    }

    [TestMethod]
    public async Task PublishVariablesDisabled_NothingPublished()
    {
        await _kernel.DisposeAsync();
        _kernel = new JavaScriptKernel(new JavaScriptKernelOptions
        {
            ForceJint = true,
            PublishVariables = false,
        });
        await _kernel.InitializeAsync();

        await _kernel.ExecuteAsync("var shouldNotPublish = 42", _context);
        var allVars = _context.Variables.GetAll();
        Assert.IsFalse(allVars.Any(v => v.Name == "shouldNotPublish"),
            "Variables should not be published when PublishVariables is false");
    }
}
