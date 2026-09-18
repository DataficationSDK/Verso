using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;

[TestClass]
[DoNotParallelize] // Console.In is process-wide
public class InputTests
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
    public async Task ConsoleReadLine_ReturnsHostInput_WithPrecedingWriteAsPrompt()
    {
        string? seenPrompt = null;
        _context.InputHandler = (prompt, _, _) =>
        {
            seenPrompt = prompt;
            return Task.FromResult<string?>("Ada");
        };

        await _kernel.ExecuteAsync(
            "System.Console.Write(\"Name: \")\nlet name = System.Console.ReadLine()", _context);

        Assert.AreEqual("Name:", seenPrompt);
        Assert.IsTrue(_context.Variables.TryGet<string>("name", out var name));
        Assert.AreEqual("Ada", name);
    }

    [TestMethod]
    public async Task ConsoleIn_IsRestoredAfterExecution()
    {
        var before = Console.In;
        _context.InputHandler = (_, _, _) => Task.FromResult<string?>("x");

        await _kernel.ExecuteAsync("let line = System.Console.ReadLine()", _context);

        Assert.AreSame(before, Console.In);
    }
}
