using Verso.Http.Kernel;
using Verso.Http.MagicCommands;
using Verso.Testing.Stubs;

namespace Verso.Http.Tests.MagicCommands;

[TestClass]
public sealed class HttpSetTimeoutMagicCommandTests
{
    [TestMethod]
    public async Task ExecuteAsync_ValidSeconds_SetsTimeout()
    {
        var cmd = new HttpSetTimeoutMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("60", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        var timeout = ctx.Variables.Get<int>(HttpKernel.TimeoutStoreKey);
        Assert.AreEqual(60, timeout);
    }

    [TestMethod]
    public async Task ExecuteAsync_NonNumeric_OutputsError()
    {
        var cmd = new HttpSetTimeoutMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("abc", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError));
    }

    [TestMethod]
    public async Task ExecuteAsync_NegativeNumber_OutputsError()
    {
        var cmd = new HttpSetTimeoutMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("-5", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError));
    }

    [TestMethod]
    public async Task ExecuteAsync_Zero_OutputsError()
    {
        var cmd = new HttpSetTimeoutMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("0", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError));
    }

    [TestMethod]
    public async Task ExecuteAsync_Empty_OutputsError()
    {
        var cmd = new HttpSetTimeoutMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError));
    }
}
