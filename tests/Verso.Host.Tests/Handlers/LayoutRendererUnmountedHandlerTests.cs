using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Layouts;
using Verso.Host.Protocol;
using Verso.Host.Tests.Layouts;

namespace Verso.Host.Tests.Handlers;

[TestClass]
public class LayoutRendererUnmountedHandlerTests
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

    private static JsonElement Params(LayoutRendererUnmountedParams p) =>
        JsonSerializer.SerializeToElement(p, JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task ValidParams_InvokesHandlerWithMatchingContext()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var extension = new RecordingLifecycleExtension(
            extensionId: "com.test.unmount.ok",
            layoutId: "sparkline");
        await ns.ExtensionHost.LoadExtensionAsync(extension);

        var frameInstanceId = ns.FrameTracker.AllocateFrameInstanceId(extension.LayoutId);
        var channel = new IframeFrameChannel(frameInstanceId, ns);

        // Mount first so the unmount has something to tear down.
        await ns.FrameTracker.InvokeMountedAsync(
            extension.ExtensionId,
            extension.LayoutId,
            frameInstanceId,
            LayoutRendererIsolation.Isolated,
            channel);

        await LayoutHandler.HandleRendererUnmountedAsync(ns, Params(new LayoutRendererUnmountedParams
        {
            NotebookId = notebookId,
            ExtensionId = extension.ExtensionId,
            LayoutId = extension.LayoutId,
            FrameInstanceId = frameInstanceId
        }));

        Assert.AreEqual(1, extension.UnmountContexts.Count);
        var ctx = extension.UnmountContexts[0];
        Assert.AreEqual(extension.ExtensionId, ctx.ExtensionId);
        Assert.AreEqual(extension.LayoutId, ctx.LayoutId);
        Assert.AreEqual(frameInstanceId, ctx.FrameInstanceId);
        Assert.AreEqual(LayoutRendererIsolation.Isolated, ctx.Isolation);
        Assert.IsFalse(channel.IsAlive, "Channel should be marked dead after unmount.");
    }

    [TestMethod]
    public async Task UnknownFrameInstance_NoOp()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        // No mount; unmount of an unknown id should not throw.
        await LayoutHandler.HandleRendererUnmountedAsync(ns, Params(new LayoutRendererUnmountedParams
        {
            NotebookId = notebookId,
            ExtensionId = "com.test.unknown",
            LayoutId = "sparkline",
            FrameInstanceId = $"{notebookId}/sparkline/999"
        }));
    }

    [TestMethod]
    public async Task HandlerThrows_ExceptionIsWrappedWithExtensionId()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        var extension = new RecordingLifecycleExtension(
            extensionId: "com.test.unmount.throws",
            layoutId: "sparkline")
        { ThrowOnUnmount = true };
        await ns.ExtensionHost.LoadExtensionAsync(extension);

        var frameInstanceId = ns.FrameTracker.AllocateFrameInstanceId(extension.LayoutId);
        var channel = new IframeFrameChannel(frameInstanceId, ns);
        await ns.FrameTracker.InvokeMountedAsync(
            extension.ExtensionId,
            extension.LayoutId,
            frameInstanceId,
            LayoutRendererIsolation.Isolated,
            channel);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            LayoutHandler.HandleRendererUnmountedAsync(ns, Params(new LayoutRendererUnmountedParams
            {
                NotebookId = notebookId,
                ExtensionId = extension.ExtensionId,
                LayoutId = extension.LayoutId,
                FrameInstanceId = frameInstanceId
            })));

        StringAssert.Contains(ex.Message, extension.ExtensionId);
        StringAssert.Contains(ex.Message, "unmount boom");
    }

    [TestMethod]
    public async Task MissingFrameInstanceId_ThrowsJsonException()
    {
        var (session, notebookId) = await CreateOpenSessionAsync();
        var ns = session.GetSession(notebookId);

        await Assert.ThrowsExceptionAsync<JsonException>(() =>
            LayoutHandler.HandleRendererUnmountedAsync(ns, Params(new LayoutRendererUnmountedParams
            {
                NotebookId = notebookId,
                ExtensionId = "com.test.anything",
                LayoutId = "sparkline",
                FrameInstanceId = ""
            })));
    }
}
