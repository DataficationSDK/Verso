using Verso.Abstractions;
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
    public async Task ConsoleReadLine_ReturnsNull_WhenPromptIsCancelled()
    {
        // A dismissed prompt answers null, the same as end of input in a terminal.
        _context.InputHandler = (_, _, _) => Task.FromResult<string?>(null);

        await _kernel.ExecuteAsync(
            "let wasCancelled = isNull (System.Console.ReadLine())", _context);

        Assert.IsTrue(_context.Variables.TryGet<bool>("wasCancelled", out var wasCancelled));
        Assert.IsTrue(wasCancelled);
    }

    [TestMethod]
    public async Task ConsoleReadLine_StopsTheCell_WhenTheCellIsInterruptedAtThePrompt()
    {
        // The user interrupting the cell while the prompt is open cancels the request with the
        // cell's own token. That has to stop the cell rather than read as a dismissed prompt.
        using var cts = new CancellationTokenSource();
        _context.CancellationToken = cts.Token;
        _context.InputHandler = (_, _, token) =>
        {
            cts.Cancel();
            return Task.FromCanceled<string?>(token);
        };

        // F# Interactive reports an exception from the cell's own code as an error output rather
        // than throwing it, so the cancellation shows as a failed cell either way.
        IReadOnlyList<CellOutput>? outputs = null;
        try
        {
            outputs = await _kernel.ExecuteAsync(
                "let line = System.Console.ReadLine()\nlet keptGoing = true", _context);
        }
        catch (OperationCanceledException)
        {
        }

        if (outputs is not null)
            Assert.IsTrue(outputs.Any(o => o.IsError), "An interrupted cell should report it stopped.");
        Assert.IsFalse(_context.Variables.TryGet<bool>("keptGoing", out _),
            "An interrupted cell should not run past its prompt.");
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
