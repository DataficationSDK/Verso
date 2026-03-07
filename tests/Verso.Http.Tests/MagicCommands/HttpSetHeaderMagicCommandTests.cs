using Verso.Http.Kernel;
using Verso.Http.MagicCommands;
using Verso.Testing.Stubs;

namespace Verso.Http.Tests.MagicCommands;

[TestClass]
public sealed class HttpSetHeaderMagicCommandTests
{
    [TestMethod]
    public async Task ExecuteAsync_ValidHeader_SetsDefaultHeader()
    {
        var cmd = new HttpSetHeaderMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("Authorization Bearer token123", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        var headers = ctx.Variables.Get<Dictionary<string, string>>(HttpKernel.DefaultHeadersStoreKey);
        Assert.IsNotNull(headers);
        Assert.AreEqual("Bearer token123", headers!["Authorization"]);
    }

    [TestMethod]
    public async Task ExecuteAsync_MultipleHeaders_Accumulate()
    {
        var cmd = new HttpSetHeaderMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("Authorization Bearer token123", ctx);
        await cmd.ExecuteAsync("Accept application/json", ctx);

        var headers = ctx.Variables.Get<Dictionary<string, string>>(HttpKernel.DefaultHeadersStoreKey);
        Assert.IsNotNull(headers);
        Assert.AreEqual(2, headers!.Count);
        Assert.AreEqual("Bearer token123", headers["Authorization"]);
        Assert.AreEqual("application/json", headers["Accept"]);
    }

    [TestMethod]
    public async Task ExecuteAsync_MissingValue_OutputsError()
    {
        var cmd = new HttpSetHeaderMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("Authorization", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError));
    }

    [TestMethod]
    public async Task ExecuteAsync_EmptyArguments_OutputsError()
    {
        var cmd = new HttpSetHeaderMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError));
    }
}
