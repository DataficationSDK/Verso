using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

namespace Verso.Host.Tests.Handlers;

/// <summary>
/// Panels cross a process boundary in the out-of-process host, so what a panel
/// produces has to survive serialization and what a user triggers has to reach the
/// panel again. These cover that round trip.
/// </summary>
[TestClass]
public class PanelHandlerTests
{
    private static async Task<(HostSession Session, NotebookSession Ns, List<string> Notifications)>
        CreateOpenSessionAsync()
    {
        var notifications = new List<string>();
        var session = new HostSession(n => notifications.Add(n));
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, session.GetSession(result.NotebookId), notifications);
    }

    private static JsonElement ToParams<T>(T value) =>
        JsonSerializer.SerializeToElement(value, JsonRpcMessage.SerializerOptions);

    [TestMethod]
    public async Task List_ReturnsAvailablePanelsWithTheirMetadata()
    {
        var (_, ns, _) = await CreateOpenSessionAsync();
        await ns.ExtensionHost.LoadExtensionAsync(new TestPanel());

        var result = await PanelHandler.HandleListAsync(ns, ToParams(new PanelListParams()));

        var panel = result.Panels.SingleOrDefault(p => p.PanelId == "findings");
        Assert.IsNotNull(panel);
        Assert.AreEqual("com.test.panelhandler", panel!.ExtensionId);
        Assert.AreEqual("Findings", panel.DisplayName);
        Assert.AreEqual("flag", panel.IconName);
        Assert.AreEqual(600, panel.Order);
    }

    [TestMethod]
    public async Task List_OmitsUnavailablePanels()
    {
        var (_, ns, _) = await CreateOpenSessionAsync();
        await ns.ExtensionHost.LoadExtensionAsync(new TestPanel { Available = false });

        var result = await PanelHandler.HandleListAsync(ns, ToParams(new PanelListParams()));

        Assert.IsFalse(result.Panels.Any(p => p.PanelId == "findings"));
    }

    [TestMethod]
    public async Task List_PanelThatThrows_IsTreatedAsUnavailable()
    {
        // One misbehaving panel must not take the whole list down with it.
        var (_, ns, _) = await CreateOpenSessionAsync();
        await ns.ExtensionHost.LoadExtensionAsync(new TestPanel { ThrowOnAvailability = true });

        var result = await PanelHandler.HandleListAsync(ns, ToParams(new PanelListParams()));

        Assert.IsFalse(result.Panels.Any(p => p.PanelId == "findings"));
    }

    [TestMethod]
    public async Task Render_ReturnsRepresentationsInOrder()
    {
        var (_, ns, _) = await CreateOpenSessionAsync();
        await ns.ExtensionHost.LoadExtensionAsync(new TestPanel());

        var result = await PanelHandler.HandleRenderAsync(ns, ToParams(new PanelRenderParams
        {
            ExtensionId = "com.test.panelhandler",
            PanelId = "findings"
        }));

        Assert.AreEqual(2, result.Representations.Count);
        Assert.AreEqual("text/html", result.Representations[0].MimeType);
        Assert.AreEqual("text/plain", result.Representations[1].MimeType);
        StringAssert.Contains(result.Representations[0].Content, "data-panel-action");
    }

    [TestMethod]
    public async Task Render_UnknownPanel_ReturnsNothing()
    {
        var (_, ns, _) = await CreateOpenSessionAsync();

        var result = await PanelHandler.HandleRenderAsync(ns, ToParams(new PanelRenderParams
        {
            ExtensionId = "com.test.nosuch",
            PanelId = "findings"
        }));

        Assert.AreEqual(0, result.Representations.Count);
    }

    [TestMethod]
    public async Task Render_PanelThatThrows_ReturnsNothingRatherThanFailing()
    {
        var (_, ns, _) = await CreateOpenSessionAsync();
        await ns.ExtensionHost.LoadExtensionAsync(new TestPanel { ThrowOnRender = true });

        var result = await PanelHandler.HandleRenderAsync(ns, ToParams(new PanelRenderParams
        {
            ExtensionId = "com.test.panelhandler",
            PanelId = "findings"
        }));

        Assert.AreEqual(0, result.Representations.Count);
    }

    [TestMethod]
    public async Task Interact_ReachesTheHandlerWithItsTarget()
    {
        var (_, ns, _) = await CreateOpenSessionAsync();
        var panel = new TestPanel();
        await ns.ExtensionHost.LoadExtensionAsync(panel);

        await PanelHandler.HandleInteractAsync(ns, ToParams(new PanelInteractParams
        {
            ExtensionId = "com.test.panelhandler",
            PanelId = "findings",
            InteractionType = "accept",
            TargetId = "f-17",
            Payload = "{\"note\":\"ok\"}"
        }));

        Assert.AreEqual(1, panel.Interactions.Count);
        Assert.AreEqual("accept", panel.Interactions[0].InteractionType);
        Assert.AreEqual("f-17", panel.Interactions[0].TargetId);
        Assert.AreEqual("{\"note\":\"ok\"}", panel.Interactions[0].Payload);
    }

    [TestMethod]
    public async Task Interact_RequestRefresh_SendsPanelUpdated()
    {
        // Without this notification the client keeps showing what it last fetched, so
        // an action that changes the panel would appear to do nothing.
        var (_, ns, notifications) = await CreateOpenSessionAsync();
        await ns.ExtensionHost.LoadExtensionAsync(new TestPanel { RefreshOnInteraction = true });

        await PanelHandler.HandleInteractAsync(ns, ToParams(new PanelInteractParams
        {
            ExtensionId = "com.test.panelhandler",
            PanelId = "findings",
            InteractionType = "accept"
        }));

        Assert.IsTrue(notifications.Any(n => n.Contains($"\"{MethodNames.PanelUpdated}\"")),
            "Expected a panel/updated notification. Got: " + string.Join(" | ", notifications));
    }

    [TestMethod]
    public async Task Interact_UnknownPanel_IsIgnored()
    {
        var (_, ns, _) = await CreateOpenSessionAsync();

        await PanelHandler.HandleInteractAsync(ns, ToParams(new PanelInteractParams
        {
            ExtensionId = "com.test.nosuch",
            PanelId = "findings",
            InteractionType = "accept"
        }));
    }

    private sealed class TestPanel : INotebookPanel, IPanelInteractionHandler
    {
        public bool Available { get; set; } = true;
        public bool ThrowOnAvailability { get; set; }
        public bool ThrowOnRender { get; set; }
        public bool RefreshOnInteraction { get; set; }
        public List<PanelInteractionContext> Interactions { get; } = new();

        public string ExtensionId => "com.test.panelhandler";
        public string Name => "Findings Panel";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;

        public string PanelId => "findings";
        public string DisplayName => "Findings";
        public string? IconName => "flag";
        public int Order => 600;

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;

        public Task<bool> IsAvailableAsync(IPanelContext context)
        {
            if (ThrowOnAvailability) throw new InvalidOperationException("boom");
            return Task.FromResult(Available);
        }

        public Task<IReadOnlyList<RenderResult>> RenderAsync(IPanelContext context)
        {
            if (ThrowOnRender) throw new InvalidOperationException("boom");

            IReadOnlyList<RenderResult> representations = new[]
            {
                new RenderResult("text/html",
                    "<button data-panel-action=\"accept\" data-target-id=\"f-17\">Accept</button>"),
                new RenderResult("text/plain", "1 open finding")
            };
            return Task.FromResult(representations);
        }

        public Task OnPanelInteractionAsync(PanelInteractionContext context)
        {
            Interactions.Add(context);
            if (RefreshOnInteraction) context.RequestRefresh();
            return Task.CompletedTask;
        }
    }
}
