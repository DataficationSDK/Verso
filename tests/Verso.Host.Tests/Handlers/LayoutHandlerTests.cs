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
    private (HostSession Session, List<string> Notifications) CreateSession()
    {
        var notifications = new List<string>();
        return (new HostSession(n => notifications.Add(n)), notifications);
    }

    private async Task<(HostSession Session, string NotebookId, List<string> Notifications)> CreateOpenSession()
    {
        var (session, notifications) = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId, notifications);
    }

    private static JsonElement InteractParams(LayoutInteractParams p) =>
        JsonSerializer.SerializeToElement(p, JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task HandleInteract_ValidHandler_InvokesWithContextFields()
    {
        var (session, notebookId, _) = await CreateOpenSession();
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
        var (session, notebookId, _) = await CreateOpenSession();
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
        var (session, notebookId, _) = await CreateOpenSession();
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
        var (session, notebookId, _) = await CreateOpenSession();
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
        var (session, notebookId, _) = await CreateOpenSession();
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
        var (session, notebookId, _) = await CreateOpenSession();

        var responseJson = await session.DispatchAsync(
            id: 1,
            method: MethodNames.LayoutInteract,
            @params: JsonSerializer.SerializeToElement(new { notebookId }));

        using var doc = JsonDocument.Parse(responseJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var err),
            $"Expected JSON-RPC error. Got: {responseJson}");
        StringAssert.Contains(err.GetProperty("message").GetString(), "extensionId");
    }

    // ─── layout/updated notification ──────────────────────────────────────────

    private static IReadOnlyList<JsonElement> ParseLayoutUpdatedNotifications(
        IReadOnlyList<string> messages)
    {
        var matches = new List<JsonElement>();
        foreach (var msg in messages)
        {
            var doc = JsonDocument.Parse(msg);
            // Notifications have no "id" field; responses do.
            if (doc.RootElement.TryGetProperty("id", out _)) continue;
            if (!doc.RootElement.TryGetProperty("method", out var method)) continue;
            if (method.GetString() != MethodNames.LayoutUpdated) continue;
            // Clone so the JsonDocument can be safely disposed by the caller's GC.
            matches.Add(doc.RootElement.Clone());
        }
        return matches;
    }

    [TestMethod]
    public async Task HandleInteract_HandlerCallsRequestRender_DispatchesFullScopeNotification()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.requestrender",
            layoutId: "dashboard");
        handler.OnInteraction = ctx => { ctx.RequestRender(); return Task.CompletedTask; };
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        await LayoutHandler.HandleInteractAsync(ns, InteractParams(new LayoutInteractParams
        {
            NotebookId = notebookId,
            ExtensionId = handler.ExtensionId,
            LayoutId = handler.LayoutId,
            FrameInstanceId = "nb-1/dashboard/0",
            InteractionType = "set-mode",
            Payload = "csharp"
        }));

        var updates = ParseLayoutUpdatedNotifications(notifications);
        Assert.AreEqual(1, updates.Count, "Expected exactly one layout/updated notification.");

        var p = updates[0].GetProperty("params");
        Assert.AreEqual(notebookId, p.GetProperty("notebookId").GetString());
        Assert.AreEqual(handler.ExtensionId, p.GetProperty("extensionId").GetString());
        Assert.AreEqual(handler.LayoutId, p.GetProperty("layoutId").GetString());
        Assert.AreEqual("nb-1/dashboard/0", p.GetProperty("frameInstanceId").GetString());
        Assert.AreEqual("full", p.GetProperty("scope").GetString());
        Assert.IsFalse(p.TryGetProperty("cellId", out _),
            "scope=full must not carry a cellId.");
    }

    [TestMethod]
    public async Task HandleInteract_HandlerCallsRequestCellRefresh_DispatchesCellScopeNotification()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var cellId = Guid.NewGuid();
        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.cellrefresh",
            layoutId: "grid");
        handler.OnInteraction = ctx => { ctx.RequestCellRefresh(cellId); return Task.CompletedTask; };
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        await LayoutHandler.HandleInteractAsync(ns, InteractParams(new LayoutInteractParams
        {
            NotebookId = notebookId,
            ExtensionId = handler.ExtensionId,
            LayoutId = handler.LayoutId,
            FrameInstanceId = "nb-1/grid/7",
            InteractionType = "move-cell",
            Payload = ""
        }));

        var updates = ParseLayoutUpdatedNotifications(notifications);
        Assert.AreEqual(1, updates.Count);

        var p = updates[0].GetProperty("params");
        Assert.AreEqual("cell", p.GetProperty("scope").GetString());
        Assert.AreEqual(cellId.ToString(), p.GetProperty("cellId").GetString());
        Assert.AreEqual("nb-1/grid/7", p.GetProperty("frameInstanceId").GetString());
    }

    [TestMethod]
    public async Task HandleInteract_HandlerDoesNotRequestUpdate_NoNotification()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.silent",
            layoutId: "dashboard");
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        await LayoutHandler.HandleInteractAsync(ns, InteractParams(new LayoutInteractParams
        {
            NotebookId = notebookId,
            ExtensionId = handler.ExtensionId,
            LayoutId = handler.LayoutId,
            FrameInstanceId = "nb-1/dashboard/0",
            InteractionType = "ping",
            Payload = ""
        }));

        var updates = ParseLayoutUpdatedNotifications(notifications);
        Assert.AreEqual(0, updates.Count,
            "A handler that does not call RequestRender/RequestCellRefresh must not dispatch layout/updated.");
    }
}
