using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Extensions.Layouts;
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

    // ─── Deprecated method forwarders ─────────────────────────────────────────

    private static DashboardLayout GetLoadedDashboard(NotebookSession ns)
    {
        return ns.ExtensionHost.GetLayouts().OfType<DashboardLayout>().Single();
    }

    [TestMethod]
    public async Task HandleUpdateCell_ForwardsToHandleInteract_AppliesSameStateAsNewPath()
    {
        // Two parallel sessions: one drives Dashboard via the deprecated method,
        // the other drives it via the new generic path. Both must converge.
        var (legacySession, legacyNotebookId, _) = await CreateOpenSession();
        var legacyNs = legacySession.GetSession(legacyNotebookId);
        var legacyDashboard = GetLoadedDashboard(legacyNs);

        var (genericSession, genericNotebookId, _) = await CreateOpenSession();
        var genericNs = genericSession.GetSession(genericNotebookId);
        var genericDashboard = GetLoadedDashboard(genericNs);

        var cellId = Guid.NewGuid();

        var legacyParams = JsonSerializer.SerializeToElement(new LayoutUpdateCellParams
        {
            CellId = cellId.ToString(),
            Row = 4,
            Col = 1,
            Width = 8,
            Height = 5
        }, JsonRpcMessage.SerializerOptions);
        await LayoutHandler.HandleUpdateCell(legacyNs, legacyParams);

        var payload = JsonSerializer.Serialize(new
        {
            cellId = cellId.ToString(),
            row = 4,
            col = 1,
            width = 8,
            height = 5
        }, JsonRpcMessage.SerializerOptions);
        await LayoutHandler.HandleInteractAsync(genericNs, InteractParams(new LayoutInteractParams
        {
            NotebookId = genericNotebookId,
            ExtensionId = "verso.layout.dashboard",
            LayoutId = "dashboard",
            FrameInstanceId = string.Empty,
            InteractionType = "updateCellPosition",
            Payload = payload
        }));

        var stubContext = new Verso.Testing.Stubs.StubVersoContext();
        var legacyContainer = await legacyDashboard.GetCellContainerAsync(cellId, stubContext);
        var genericContainer = await genericDashboard.GetCellContainerAsync(cellId, stubContext);

        Assert.AreEqual(genericContainer.X, legacyContainer.X);
        Assert.AreEqual(genericContainer.Y, legacyContainer.Y);
        Assert.AreEqual(genericContainer.Width, legacyContainer.Width);
        Assert.AreEqual(genericContainer.Height, legacyContainer.Height);
        Assert.AreEqual(1.0, legacyContainer.X);
        Assert.AreEqual(4.0, legacyContainer.Y);
        Assert.AreEqual(8.0, legacyContainer.Width);
        Assert.AreEqual(5.0, legacyContainer.Height);
    }

    [TestMethod]
    public async Task HandleSetEditMode_ForwardsToHandleInteract_TogglesIsEditMode()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);
        var dashboard = GetLoadedDashboard(ns);
        dashboard.IsEditMode = false;

        Assert.IsFalse(dashboard.IsEditMode);

        await LayoutHandler.HandleSetEditMode(ns, JsonSerializer.SerializeToElement(
            new LayoutSetEditModeParams { EditMode = true }, JsonRpcMessage.SerializerOptions));
        Assert.IsTrue(dashboard.IsEditMode);

        await LayoutHandler.HandleSetEditMode(ns, JsonSerializer.SerializeToElement(
            new LayoutSetEditModeParams { EditMode = false }, JsonRpcMessage.SerializerOptions));
        Assert.IsFalse(dashboard.IsEditMode);
    }

    // ─── layout/getRendererPackage ────────────────────────────────────────────

    private static JsonElement GetPackageParams(string notebookId, string extensionId, string layoutId) =>
        JsonSerializer.SerializeToElement(new LayoutGetRendererPackageParams
        {
            NotebookId = notebookId,
            ExtensionId = extensionId,
            LayoutId = layoutId
        }, JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task HandleGetRendererPackage_IsolatedFixture_ReturnsEncodedFilesAndCsp()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeIsolatedLayoutEngine(
            extensionId: "com.test.layout.iso.pkg",
            layoutId: "iso-pkg");
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var result = await LayoutHandler.HandleGetRendererPackageAsync(
            ns, GetPackageParams(notebookId, fake.ExtensionId, fake.LayoutId));

        Assert.IsNotNull(result, "Isolated layout should return a package.");
        Assert.AreEqual(FakeIsolatedLayoutEngine.DefaultEntryPoint, result!.EntryPoint);
        Assert.AreEqual(FakeIsolatedLayoutEngine.DefaultContentSecurityPolicy,
            result.ContentSecurityPolicy);
        Assert.IsTrue(result.Files.TryGetValue(FakeIsolatedLayoutEngine.DefaultEntryPoint,
                out var base64),
            "Result must contain the entry file.");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64!));
        Assert.AreEqual(FakeIsolatedLayoutEngine.DefaultMainJs, decoded);
    }

    [TestMethod]
    public async Task HandleGetRendererPackage_InlineLayout_ReturnsNull()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        // FakeLayoutInteractionHandler implements ILayoutEngine with the default
        // RendererIsolation (Inline), so it stands in as the inline fixture here.
        var fake = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.inline.pkg",
            layoutId: "inline-pkg");
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var result = await LayoutHandler.HandleGetRendererPackageAsync(
            ns, GetPackageParams(notebookId, fake.ExtensionId, fake.LayoutId));

        Assert.IsNull(result, "Inline layout must not return a renderer package.");
    }

    [TestMethod]
    public async Task HandleGetRendererPackage_IsolatedFixtureWithoutPackage_ReturnsNull()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeIsolatedLayoutEngine(
            extensionId: "com.test.layout.iso.nopkg",
            layoutId: "iso-nopkg")
        {
            Package = null
        };
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var result = await LayoutHandler.HandleGetRendererPackageAsync(
            ns, GetPackageParams(notebookId, fake.ExtensionId, fake.LayoutId));

        Assert.IsNull(result,
            "An isolated layout that returns no package should surface as null at the wire.");
    }

    [TestMethod]
    public async Task HandleGetRendererPackage_UnknownLayout_ThrowsWithSymbolicCode()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => LayoutHandler.HandleGetRendererPackageAsync(
                ns, GetPackageParams(notebookId, "com.unknown.layout", "ghost")));

        StringAssert.Contains(ex.Message, "LAYOUT_ENGINE_NOT_FOUND");
        StringAssert.Contains(ex.Message, "com.unknown.layout");
        StringAssert.Contains(ex.Message, "ghost");
    }

    [TestMethod]
    public async Task HandleGetRendererPackage_DispatchedThroughHostSession_RoutesViaMethodName()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeIsolatedLayoutEngine(
            extensionId: "com.test.layout.iso.dispatch",
            layoutId: "iso-dispatch");
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var responseJson = await session.DispatchAsync(
            id: 1,
            method: MethodNames.LayoutGetRendererPackage,
            @params: GetPackageParams(notebookId, fake.ExtensionId, fake.LayoutId));

        using var doc = JsonDocument.Parse(responseJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("result", out var result),
            $"Expected a successful JSON-RPC response. Got: {responseJson}");
        Assert.AreEqual(FakeIsolatedLayoutEngine.DefaultEntryPoint,
            result.GetProperty("entryPoint").GetString());
    }

    [TestMethod]
    public async Task HandleGetLayouts_IsolatedFixture_EmitsIsolatedRendererIsolation()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeIsolatedLayoutEngine(
            extensionId: "com.test.layout.iso.dto",
            layoutId: "iso-dto");
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var result = LayoutHandler.HandleGetLayouts(ns);
        var dto = result.Layouts.Single(l =>
            l.ExtensionId == fake.ExtensionId && l.Id == fake.LayoutId);
        Assert.AreEqual("isolated", dto.RendererIsolation);
    }

    [TestMethod]
    public async Task HandleGetLayouts_InlineFixture_EmitsInlineRendererIsolation()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.inline.dto",
            layoutId: "inline-dto");
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var result = LayoutHandler.HandleGetLayouts(ns);
        var dto = result.Layouts.Single(l =>
            l.ExtensionId == fake.ExtensionId && l.Id == fake.LayoutId);
        Assert.AreEqual("inline", dto.RendererIsolation);
    }

    // ─── layout/getStaticAssets ───────────────────────────────────────────────

    private static JsonElement GetStaticAssetsParams(
        string notebookId, string extensionId, string layoutId,
        params string[] supportedAssetContentTypes) =>
        JsonSerializer.SerializeToElement(new LayoutGetStaticAssetsParams
        {
            NotebookId = notebookId,
            ExtensionId = extensionId,
            LayoutId = layoutId,
            SupportedAssetContentTypes = supportedAssetContentTypes.ToList(),
            SupportedRenderFormats = new List<string> { "text/html" }
        }, JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task HandleGetStaticAssets_EngineWithAssets_ReturnsBase64Encoded()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.assets",
            layoutId: "with-assets")
        {
            StaticAssets = new[]
            {
                new LayoutStaticAsset(
                    "main.css", "text/css",
                    System.Text.Encoding.UTF8.GetBytes(":root { color: red; }"))
            }
        };
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var result = await LayoutHandler.HandleGetStaticAssetsAsync(
            ns, GetStaticAssetsParams(notebookId, fake.ExtensionId, fake.LayoutId, "text/css"));

        Assert.AreEqual(1, result.Assets.Count);
        Assert.AreEqual("main.css", result.Assets[0].AssetId);
        Assert.AreEqual("text/css", result.Assets[0].ContentType);
        var decoded = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(result.Assets[0].Content));
        Assert.AreEqual(":root { color: red; }", decoded);
    }

    [TestMethod]
    public async Task HandleGetStaticAssets_EngineWithoutAssets_ReturnsEmpty()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.noassets",
            layoutId: "no-assets");
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var result = await LayoutHandler.HandleGetStaticAssetsAsync(
            ns, GetStaticAssetsParams(notebookId, fake.ExtensionId, fake.LayoutId, "text/css"));

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Assets.Count);
    }

    [TestMethod]
    public async Task HandleGetStaticAssets_UnsupportedContentType_FilteredOut()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.filter",
            layoutId: "filter")
        {
            StaticAssets = new[]
            {
                new LayoutStaticAsset("a.css", "text/css", new byte[] { 1 }),
                new LayoutStaticAsset("script.js", "text/javascript", new byte[] { 2 })
            }
        };
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        // Host advertises only text/css; the JS asset must be dropped defensively.
        var result = await LayoutHandler.HandleGetStaticAssetsAsync(
            ns, GetStaticAssetsParams(notebookId, fake.ExtensionId, fake.LayoutId, "text/css"));

        Assert.AreEqual(1, result.Assets.Count);
        Assert.AreEqual("a.css", result.Assets[0].AssetId);
    }

    [TestMethod]
    public async Task HandleGetStaticAssets_CapabilitiesRoundTripToEngine()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.caps",
            layoutId: "caps");
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        await LayoutHandler.HandleGetStaticAssetsAsync(
            ns, GetStaticAssetsParams(notebookId, fake.ExtensionId, fake.LayoutId,
                "text/css", "application/x-tela-skin"));

        Assert.IsNotNull(fake.LastObservedCapabilities);
        Assert.IsTrue(fake.LastObservedCapabilities!.SupportedAssetContentTypes.Contains("text/css"));
        Assert.IsTrue(fake.LastObservedCapabilities.SupportedAssetContentTypes
            .Contains("application/x-tela-skin"));
        Assert.IsTrue(fake.LastObservedCapabilities.SupportedRenderFormats.Contains("text/html"));
    }

    [TestMethod]
    public async Task HandleGetStaticAssets_UnknownLayout_ThrowsWithSymbolicCode()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => LayoutHandler.HandleGetStaticAssetsAsync(
                ns, GetStaticAssetsParams(notebookId, "com.unknown.layout", "ghost", "text/css")));

        StringAssert.Contains(ex.Message, "LAYOUT_ENGINE_NOT_FOUND");
        StringAssert.Contains(ex.Message, "com.unknown.layout");
        StringAssert.Contains(ex.Message, "ghost");
    }

    [TestMethod]
    public async Task HandleGetStaticAssets_MissingExtensionId_ThrowsJsonException()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        await Assert.ThrowsExceptionAsync<JsonException>(
            () => LayoutHandler.HandleGetStaticAssetsAsync(
                ns, GetStaticAssetsParams(notebookId, "", "anything", "text/css")));
    }

    [TestMethod]
    public async Task HandleGetStaticAssets_DispatchedThroughHostSession_RoutesViaMethodName()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.assets.dispatch",
            layoutId: "assets-dispatch")
        {
            StaticAssets = new[]
            {
                new LayoutStaticAsset("a.css", "text/css", new byte[] { 1, 2, 3 })
            }
        };
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var responseJson = await session.DispatchAsync(
            id: 1,
            method: MethodNames.LayoutGetStaticAssets,
            @params: GetStaticAssetsParams(notebookId, fake.ExtensionId, fake.LayoutId, "text/css"));

        using var doc = JsonDocument.Parse(responseJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("result", out var result),
            $"Expected a successful JSON-RPC response. Got: {responseJson}");
        var assets = result.GetProperty("assets");
        Assert.AreEqual(1, assets.GetArrayLength());
        Assert.AreEqual("a.css", assets[0].GetProperty("assetId").GetString());
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

    [TestMethod]
    public async Task HandleGetStaticAssets_LoadHintsAndCsp_RoundTripThroughDto()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.scripthints",
            layoutId: "script-hints")
        {
            StaticAssets = new[]
            {
                new LayoutStaticAsset("main.js", "text/javascript", new byte[] { 1, 2 })
                {
                    LoadHints = new LayoutStaticAssetLoadHints(
                        LayoutScriptModuleKind.Module,
                        LayoutScriptLoadMode.Async,
                        LayoutScriptPlacement.BeforeLayoutHtml),
                    ContentSecurityPolicy = "script-src 'self'",
                },
            },
        };
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var result = await LayoutHandler.HandleGetStaticAssetsAsync(
            ns, GetStaticAssetsParams(notebookId, fake.ExtensionId, fake.LayoutId,
                "text/javascript"));

        Assert.AreEqual(1, result.Assets.Count);
        var dto = result.Assets[0];
        Assert.IsNotNull(dto.LoadHints);
        Assert.AreEqual("module", dto.LoadHints!.ModuleKind);
        Assert.AreEqual("async", dto.LoadHints.LoadMode);
        Assert.AreEqual("beforeLayoutHtml", dto.LoadHints.Placement);
        Assert.AreEqual("script-src 'self'", dto.ContentSecurityPolicy);
    }

    [TestMethod]
    public async Task HandleGetStaticAssets_NoLoadHints_DtoFieldIsNull()
    {
        var (session, notebookId, _) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var fake = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.nohints",
            layoutId: "no-hints")
        {
            StaticAssets = new[]
            {
                new LayoutStaticAsset("plain.css", "text/css", new byte[] { 1 }),
            },
        };
        await ns.ExtensionHost.LoadExtensionAsync(fake);

        var result = await LayoutHandler.HandleGetStaticAssetsAsync(
            ns, GetStaticAssetsParams(notebookId, fake.ExtensionId, fake.LayoutId, "text/css"));

        Assert.AreEqual(1, result.Assets.Count);
        Assert.IsNull(result.Assets[0].LoadHints);
        Assert.IsNull(result.Assets[0].ContentSecurityPolicy);
    }

    [TestMethod]
    public async Task HandleInteract_ReservedRequestRender_BypassesHandlerAndDispatchesFullScope()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.reserved",
            layoutId: "reserved");
        handler.OnInteraction = _ =>
            throw new InvalidOperationException("Handler must NOT be invoked for reserved verso/* interactions.");
        await ns.ExtensionHost.LoadExtensionAsync(handler);

        await LayoutHandler.HandleInteractAsync(ns, InteractParams(new LayoutInteractParams
        {
            NotebookId = notebookId,
            ExtensionId = handler.ExtensionId,
            LayoutId = handler.LayoutId,
            FrameInstanceId = "nb-1/reserved/0",
            InteractionType = "verso/requestRender",
            Payload = "",
        }));

        Assert.AreEqual(0, handler.ReceivedInteractions.Count,
            "Reserved verso/* interactions must be intercepted before the handler runs.");

        var updates = ParseLayoutUpdatedNotifications(notifications);
        Assert.AreEqual(1, updates.Count);
        var p = updates[0].GetProperty("params");
        Assert.AreEqual(handler.ExtensionId, p.GetProperty("extensionId").GetString());
        Assert.AreEqual(handler.LayoutId, p.GetProperty("layoutId").GetString());
        Assert.AreEqual("nb-1/reserved/0", p.GetProperty("frameInstanceId").GetString());
        Assert.AreEqual("full", p.GetProperty("scope").GetString());
    }

    [TestMethod]
    public async Task HandleInteract_ReservedRequestRender_WithoutRegisteredHandler_StillDispatches()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        // No handler is registered for this layout id. The reserved-interaction shortcut
        // must still fire so author scripts can request a re-render without forcing the
        // layout to implement ILayoutInteractionHandler.
        await LayoutHandler.HandleInteractAsync(ns, InteractParams(new LayoutInteractParams
        {
            NotebookId = notebookId,
            ExtensionId = "com.test.layout.noregistration",
            LayoutId = "no-handler",
            FrameInstanceId = "nb-1/noh/0",
            InteractionType = "verso/requestRender",
            Payload = "",
        }));

        var updates = ParseLayoutUpdatedNotifications(notifications);
        Assert.AreEqual(1, updates.Count);
        Assert.AreEqual("full", updates[0].GetProperty("params").GetProperty("scope").GetString());
    }

    [TestMethod]
    public async Task HandleInteract_UnknownReservedType_DropsSilentlyWithoutNotification()
    {
        var (session, notebookId, notifications) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        await LayoutHandler.HandleInteractAsync(ns, InteractParams(new LayoutInteractParams
        {
            NotebookId = notebookId,
            ExtensionId = "com.test.layout.unkreserved",
            LayoutId = "unk",
            FrameInstanceId = "",
            InteractionType = "verso/madeUpFutureAction",
            Payload = "",
        }));

        Assert.AreEqual(0, ParseLayoutUpdatedNotifications(notifications).Count,
            "Unknown verso/* types must be dropped silently so older hosts don't throw when clients learn new ones.");
    }
}
