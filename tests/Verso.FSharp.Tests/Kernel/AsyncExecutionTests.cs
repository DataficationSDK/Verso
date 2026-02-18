using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;

[TestClass]
public class AsyncExecutionTests
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
    public async Task AsyncWorkflow_Evaluates()
    {
        var outputs = await _kernel.ExecuteAsync(
            "async { return 42 } |> Async.RunSynchronously", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("42"), $"Expected '42', got: {allText}");
    }

    [TestMethod]
    public async Task TaskResult_Evaluates()
    {
        var outputs = await _kernel.ExecuteAsync(
            "System.Threading.Tasks.Task.FromResult(99).Result", _context);
        var allText = string.Join(" ", outputs.Select(o => o.Content));
        Assert.IsTrue(allText.Contains("99"), $"Expected '99', got: {allText}");
    }

    [TestMethod]
    public async Task Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _context.CancellationToken = cts.Token;

        var ex = await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () =>
        {
            await _kernel.ExecuteAsync("let x = 1", _context);
        });
        Assert.IsInstanceOfType(ex, typeof(OperationCanceledException));
    }

    [TestMethod]
    public async Task ConcurrentExecution_Serialized()
    {
        // Two concurrent executions should be serialized via the semaphore
        var task1 = _kernel.ExecuteAsync("let a = 1", _context);
        var task2 = _kernel.ExecuteAsync("let b = 2", _context);

        var results = await Task.WhenAll(task1, task2);

        // Both should succeed without errors
        Assert.IsFalse(results[0].Any(o => o.IsError), "First execution had errors");
        Assert.IsFalse(results[1].Any(o => o.IsError), "Second execution had errors");
    }
}
