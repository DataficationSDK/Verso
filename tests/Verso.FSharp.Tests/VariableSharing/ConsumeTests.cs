using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.FSharp.Tests.Helpers;

namespace Verso.FSharp.Tests.VariableSharing;

[TestClass]
public class ConsumeTests
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
    public async Task VariablesGet_RetrievesFromStore()
    {
        _context.Variables.Set("externalValue", 42);

        var outputs = await _kernel.ExecuteAsync(
            "let v = Variables.Get<int>(\"externalValue\")\nv", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("42"), $"Expected '42', got: {allText}");
    }

    [TestMethod]
    public async Task VariablesTryGet_ReturnsTrue()
    {
        _context.Variables.Set("existingVar", "hello");

        var outputs = await _kernel.ExecuteAsync(
            @"let mutable result = """"
let mutable value = Unchecked.defaultof<string>
if Variables.TryGet<string>(""existingVar"", &value) then
    result <- value
result", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("hello"), $"Expected 'hello', got: {allText}");
    }

    [TestMethod]
    public async Task TryGetVar_Some_WhenExists()
    {
        _context.Variables.Set("testKey", 99);

        // tryGetVar is available without 'open VersoHelpers' due to [<AutoOpen>]
        var outputs = await _kernel.ExecuteAsync(
            "let r = tryGetVar<int> \"testKey\"\nr", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("Some") && allText.Contains("99"),
            $"Expected 'Some 99', got: {allText}");
    }

    [TestMethod]
    public async Task TryGetVar_None_WhenMissing()
    {
        // tryGetVar is available without 'open VersoHelpers' due to [<AutoOpen>]
        var outputs = await _kernel.ExecuteAsync(
            "let r = tryGetVar<int> \"nonexistent\"\nr", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("None"), $"Expected 'None', got: {allText}");
    }

    [TestMethod]
    public async Task CrossKernel_VariableSharing()
    {
        // Simulate a C# kernel setting a variable
        _context.Variables.Set("fromCSharp", "shared data");

        // F# kernel reads it
        var outputs = await _kernel.ExecuteAsync(
            "let data = Variables.Get<string>(\"fromCSharp\")\nprintfn \"%s\" data", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("shared data"), $"Expected 'shared data', got: {allText}");

        // F# kernel writes a variable
        await _kernel.ExecuteAsync("let fromFSharp = 123", _context);
        Assert.IsTrue(_context.Variables.TryGet<int>("fromFSharp", out var val));
        Assert.AreEqual(123, val);
    }
}
