using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;
using Verso.Testing.Fakes;

namespace Verso.Host.Tests.Handlers;

[TestClass]
public class LayoutHandlerTests
{
    private HostSession CreateSession()
    {
        var notifications = new List<string>();
        return new HostSession(n => notifications.Add(n));
    }

    private async Task<(HostSession Session, string NotebookId)> CreateOpenSession()
    {
        var session = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId);
    }

    private static JsonElement InteractParams(LayoutInteractParams p) =>
        JsonSerializer.SerializeToElement(p, JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task HandleInteract_ValidHandler_InvokesWithContextFields()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.dispatch",
            layoutId: "dashboard");
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        var result = await LayoutHandler.HandleInteractAsync(ns, InteractParams(new LayoutInteractParams
        {
            NotebookId = notebookId,
            ExtensionId = handler.ExtensionId,
            LayoutId = handler.LayoutId,
            FrameInstanceId = "nb-1/dashboard/0",
            InteractionType = "setEditMode",
            Payload = "true",
            TargetId = "edit-toggle"
        }));

        Assert.IsNull(result, "layout/interact returns null on success.");
        Assert.AreEqual(1, handler.ReceivedInteractions.Count);

        var ctx = handler.ReceivedInteractions[0];
        Assert.AreEqual(handler.ExtensionId, ctx.ExtensionId);
        Assert.AreEqual(handler.LayoutId, ctx.LayoutId);
        Assert.AreEqual("nb-1/dashboard/0", ctx.FrameInstanceId);
        Assert.AreEqual("setEditMode", ctx.InteractionType);
        Assert.AreEqual("true", ctx.Payload);
        Assert.AreEqual("edit-toggle", ctx.TargetId);
        Assert.IsNotNull(ctx.Verso);
    }

    [TestMethod]
    public async Task HandleInteract_FrameInstanceIdRoundTrips()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.frame",
            layoutId: "grid");
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        var frameId = "nb-1/grid/42";

        await LayoutHandler.HandleInteractAsync(ns, InteractParams(new LayoutInteractParams
        {
            NotebookId = notebookId,
            ExtensionId = handler.ExtensionId,
            LayoutId = handler.LayoutId,
            FrameInstanceId = frameId,
            InteractionType = "click",
            Payload = ""
        }));

        Assert.AreEqual(frameId, handler.ReceivedInteractions[0].FrameInstanceId);
    }

    [TestMethod]
    public async Task HandleInteract_MissingHandler_ThrowsWithSymbolicCode()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => LayoutHandler.HandleInteractAsync(ns, InteractParams(new LayoutInteractParams
            {
                NotebookId = notebookId,
                ExtensionId = "com.unknown.layout",
                LayoutId = "ghost",
                FrameInstanceId = "nb-1/ghost/0",
                InteractionType = "click",
                Payload = ""
            })));

        StringAssert.Contains(ex.Message, "LAYOUT_INTERACTION_HANDLER_NOT_FOUND");
        StringAssert.Contains(ex.Message, "com.unknown.layout");
        StringAssert.Contains(ex.Message, "ghost");
    }

    [TestMethod]
    public async Task HandleInteract_HandlerThrows_SurfacedWithExtensionId()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.boom",
            layoutId: "dashboard");
        handler.OnInteraction = _ => throw new InvalidOperationException("boom");
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => LayoutHandler.HandleInteractAsync(ns, InteractParams(new LayoutInteractParams
            {
                NotebookId = notebookId,
                ExtensionId = handler.ExtensionId,
                LayoutId = handler.LayoutId,
                FrameInstanceId = "nb-1/dashboard/0",
                InteractionType = "explode",
                Payload = ""
            })));

        StringAssert.Contains(ex.Message, handler.ExtensionId,
            "Surfaced error must include the originating extension id.");
        StringAssert.Contains(ex.Message, "boom");
    }

    [TestMethod]
    public async Task HandleInteract_DispatchedThroughHostSession_RoutesViaMethodName()
    {
        // End-to-end through HostSession.DispatchAsync to confirm the
        // "layout/interact" method name is wired to LayoutHandler.HandleInteractAsync.
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.routed",
            layoutId: "dashboard");
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        var responseJson = await session.DispatchAsync(
            id: 1,
            method: MethodNames.LayoutInteract,
            @params: InteractParams(new LayoutInteractParams
            {
                NotebookId = notebookId,
                ExtensionId = handler.ExtensionId,
                LayoutId = handler.LayoutId,
                FrameInstanceId = "nb-1/dashboard/0",
                InteractionType = "ping",
                Payload = "{}"
            }));

        using var doc = JsonDocument.Parse(responseJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("result", out _),
            $"Expected a successful JSON-RPC response. Got: {responseJson}");
        Assert.AreEqual(1, handler.ReceivedInteractions.Count);
        Assert.AreEqual("ping", handler.ReceivedInteractions[0].InteractionType);
    }

    [TestMethod]
    public async Task HandleInteract_MissingParams_ReturnsJsonError()
    {
        var (session, notebookId) = await CreateOpenSession();

        var responseJson = await session.DispatchAsync(
            id: 1,
            method: MethodNames.LayoutInteract,
            @params: JsonSerializer.SerializeToElement(new { notebookId }));

        using var doc = JsonDocument.Parse(responseJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var err),
            $"Expected JSON-RPC error. Got: {responseJson}");
        StringAssert.Contains(err.GetProperty("message").GetString(), "extensionId");
    }
}
