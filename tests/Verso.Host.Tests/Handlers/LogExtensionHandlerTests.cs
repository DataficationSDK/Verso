using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

namespace Verso.Host.Tests.Handlers;

[TestClass]
public class LogExtensionHandlerTests
{
    private async Task<(HostSession Session, string NotebookId)> CreateOpenSessionAsync()
    {
        var session = new HostSession(_ => { });
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId);
    }

    private static JsonElement Params(LogExtensionParams p) =>
        JsonSerializer.SerializeToElement(p, JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task ValidParams_WritesFrameInstancePrefixedLine()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            LogHandler.HandleExtensionLog(ns, Params(new LogExtensionParams
            {
                NotebookId = notebookId,
                ExtensionId = "com.test.logger",
                LayoutId = "sparkline",
                FrameInstanceId = $"{notebookId}/sparkline/1",
                Level = "warn",
                Message = "hello from the iframe"
            }));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        StringAssert.Contains(output, $"[frame:{notebookId}/sparkline/1]");
        StringAssert.Contains(output, "[warn]");
        StringAssert.Contains(output, "[com.test.logger/sparkline]");
        StringAssert.Contains(output, "hello from the iframe");
    }

    [TestMethod]
    public async Task MissingFrameInstanceId_ThrowsJsonException()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        Assert.ThrowsException<JsonException>(() =>
            LogHandler.HandleExtensionLog(ns, Params(new LogExtensionParams
            {
                NotebookId = notebookId,
                ExtensionId = "com.test.logger",
                LayoutId = "sparkline",
                FrameInstanceId = "",
                Level = "info",
                Message = "x"
            })));
    }

    [TestMethod]
    public async Task MissingLevel_ThrowsJsonException()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        Assert.ThrowsException<JsonException>(() =>
            LogHandler.HandleExtensionLog(ns, Params(new LogExtensionParams
            {
                NotebookId = notebookId,
                ExtensionId = "com.test.logger",
                LayoutId = "sparkline",
                FrameInstanceId = $"{notebookId}/sparkline/1",
                Level = "",
                Message = "x"
            })));
    }

    [TestMethod]
    public async Task NullParams_ThrowsJsonException()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        Assert.ThrowsException<JsonException>(() =>
            LogHandler.HandleExtensionLog(ns, null));
    }
}
