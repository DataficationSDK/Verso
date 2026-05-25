using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;
using Verso.Testing.Fakes;

namespace Verso.Host.Tests.Handlers;

[TestClass]
public class LayoutAllocateFrameInstanceHandlerTests
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

    private static JsonElement Params(LayoutAllocateFrameInstanceParams p) =>
        JsonSerializer.SerializeToElement(p, JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task ValidParams_ReturnsTokenWithNotebookLayoutSeqShape()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var engine = new FakeIsolatedLayoutEngine(
            extensionId: "com.test.allocate.ok",
            layoutId: "sparkline");
        await ns.ExtensionHost.LoadExtensionAsync(engine);

        var first = LayoutHandler.HandleAllocateFrameInstance(ns, Params(new LayoutAllocateFrameInstanceParams
        {
            NotebookId = notebookId,
            ExtensionId = engine.ExtensionId,
            LayoutId = engine.LayoutId
        }));

        var second = LayoutHandler.HandleAllocateFrameInstance(ns, Params(new LayoutAllocateFrameInstanceParams
        {
            NotebookId = notebookId,
            ExtensionId = engine.ExtensionId,
            LayoutId = engine.LayoutId
        }));

        Assert.AreEqual($"{notebookId}/sparkline/1", first.FrameInstanceId);
        Assert.AreEqual($"{notebookId}/sparkline/2", second.FrameInstanceId);
    }

    [TestMethod]
    public async Task UnknownEngine_ThrowsLayoutEngineNotFound()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
            LayoutHandler.HandleAllocateFrameInstance(ns, Params(new LayoutAllocateFrameInstanceParams
            {
                NotebookId = notebookId,
                ExtensionId = "com.test.missing",
                LayoutId = "sparkline"
            })));

        StringAssert.Contains(ex.Message, "LAYOUT_ENGINE_NOT_FOUND");
    }

    [TestMethod]
    public async Task InlineEngine_ThrowsLayoutNotIsolated()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        // FakeLayoutInteractionHandler is an inline layout engine.
        var inline = new FakeLayoutInteractionHandler(
            extensionId: "com.test.allocate.inline",
            layoutId: "grid");
        await ns.ExtensionHost.LoadExtensionAsync(inline);

        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
            LayoutHandler.HandleAllocateFrameInstance(ns, Params(new LayoutAllocateFrameInstanceParams
            {
                NotebookId = notebookId,
                ExtensionId = inline.ExtensionId,
                LayoutId = inline.LayoutId
            })));

        StringAssert.Contains(ex.Message, "LAYOUT_NOT_ISOLATED");
    }

    [TestMethod]
    public async Task MissingParams_ThrowsJsonException()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        Assert.ThrowsException<JsonException>(() =>
            LayoutHandler.HandleAllocateFrameInstance(ns, null));
    }
}
