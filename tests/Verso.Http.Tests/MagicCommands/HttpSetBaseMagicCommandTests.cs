using Verso.Http.Kernel;
using Verso.Http.MagicCommands;
using Verso.Testing.Stubs;

namespace Verso.Http.Tests.MagicCommands;

[TestClass]
public sealed class HttpSetBaseMagicCommandTests
{
    [TestMethod]
    public async Task ExecuteAsync_ValidUrl_SetsVariableAndOutputsConfirmation()
    {
        var cmd = new HttpSetBaseMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("https://api.example.com", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        var url = ctx.Variables.Get<string>(HttpKernel.BaseUrlStoreKey);
        Assert.AreEqual("https://api.example.com", url);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.Content.Contains("https://api.example.com")));
    }

    [TestMethod]
    public async Task ExecuteAsync_EmptyUrl_OutputsError()
    {
        var cmd = new HttpSetBaseMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhitespaceUrl_OutputsError()
    {
        var cmd = new HttpSetBaseMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("   ", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError));
    }
}
