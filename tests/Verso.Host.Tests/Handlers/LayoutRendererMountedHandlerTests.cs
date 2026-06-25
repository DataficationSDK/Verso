using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;
using Verso.Host.Tests.Layouts;
using Verso.Testing.Fakes;

namespace Verso.Host.Tests.Handlers;

[TestClass]
public class LayoutRendererMountedHandlerTests
{
    private async Task<(HostSession Session, string NotebookId, List<string> Notifications)> CreateOpenSessionAsync()
    {
        var notifications = new List<string>();
        var session = new HostSession(n => notifications.Add(n));
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId, notifications);
    }

    private static JsonElement Params(LayoutRendererMountedParams p) =>
        JsonSerializer.SerializeToElement(p, JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task LifecycleHandlerRegistered_ReturnsExtensionPayloadAndPassesChannel()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var extension = new RecordingLifecycleExtension(
            extensionId: "com.test.mounted.ok",
            layoutId: "sparkline",
            initPayload: new Dictionary<string, object> { ["spec"] = "demo" });
        await ns.ExtensionHost.LoadExtensionAsync(extension);

        var frameInstanceId = ns.FrameTracker.AllocateFrameInstanceId(extension.LayoutId);

        var result = await LayoutHandler.HandleRendererMountedAsync(ns, Params(new LayoutRendererMountedParams
        {
            NotebookId = notebookId,
            ExtensionId = extension.ExtensionId,
            LayoutId = extension.LayoutId,
            FrameInstanceId = frameInstanceId
        }));

        Assert.IsNotNull(result.Extension);
        Assert.AreEqual("demo", result.Extension!["spec"]);
        Assert.AreEqual(1, extension.MountContexts.Count);

        var ctx = extension.MountContexts[0];
        Assert.AreEqual(extension.ExtensionId, ctx.ExtensionId);
        Assert.AreEqual(extension.LayoutId, ctx.LayoutId);
        Assert.AreEqual(frameInstanceId, ctx.FrameInstanceId);
        Assert.AreEqual(LayoutRendererIsolation.Isolated, ctx.Isolation);
        Assert.IsNotNull(ctx.Frame);
        Assert.AreEqual(frameInstanceId, ctx.Frame.FrameInstanceId);
        Assert.IsTrue(ctx.Frame.IsAlive);
    }

    [TestMethod]
    public async Task NoLifecycleHandler_ReturnsNullExtension()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var engine = new FakeIsolatedLayoutEngine(
            extensionId: "com.test.mounted.engineonly",
            layoutId: "sparkline");
        await ns.ExtensionHost.LoadExtensionAsync(engine);

        var frameInstanceId = ns.FrameTracker.AllocateFrameInstanceId(engine.LayoutId);

        var result = await LayoutHandler.HandleRendererMountedAsync(ns, Params(new LayoutRendererMountedParams
        {
            NotebookId = notebookId,
            ExtensionId = engine.ExtensionId,
            LayoutId = engine.LayoutId,
            FrameInstanceId = frameInstanceId
        }));

        Assert.IsNull(result.Extension);
    }

    [TestMethod]
    public async Task HandlerThrows_ExceptionIsWrappedWithExtensionId()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var extension = new RecordingLifecycleExtension(
            extensionId: "com.test.mounted.throws",
            layoutId: "sparkline")
        { ThrowOnMount = true };
        await ns.ExtensionHost.LoadExtensionAsync(extension);

        var frameInstanceId = ns.FrameTracker.AllocateFrameInstanceId(extension.LayoutId);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            LayoutHandler.HandleRendererMountedAsync(ns, Params(new LayoutRendererMountedParams
            {
                NotebookId = notebookId,
                ExtensionId = extension.ExtensionId,
                LayoutId = extension.LayoutId,
                FrameInstanceId = frameInstanceId
            })));

        StringAssert.Contains(ex.Message, extension.ExtensionId);
        StringAssert.Contains(ex.Message, "mount boom");
    }

    [TestMethod]
    public async Task MissingFrameInstanceId_ThrowsJsonException()
    {
        var (session, notebookId, _) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        await Assert.ThrowsExceptionAsync<JsonException>(() =>
            LayoutHandler.HandleRendererMountedAsync(ns, Params(new LayoutRendererMountedParams
            {
                NotebookId = notebookId,
                ExtensionId = "com.test.anything",
                LayoutId = "sparkline",
                FrameInstanceId = ""
            })));
    }

}
