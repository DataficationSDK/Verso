using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;
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

    private sealed class RecordingLifecycleExtension : IExtension, ILayoutEngine, ILayoutLifecycleHandler
    {
        private readonly IDictionary<string, object>? _initPayload;

        public RecordingLifecycleExtension(
            string extensionId,
            string layoutId,
            IDictionary<string, object>? initPayload = null)
        {
            ExtensionId = extensionId;
            LayoutId = layoutId;
            _initPayload = initPayload;
        }

        public string ExtensionId { get; }
        public string Name => "Recording Lifecycle";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public string LayoutId { get; }
        public string DisplayName => Name;
        public string? Icon => null;
        public LayoutCapabilities Capabilities => LayoutCapabilities.None;
        public bool RequiresCustomRenderer => true;
        public LayoutRendererIsolation RendererIsolation => LayoutRendererIsolation.Isolated;

        public List<LayoutRendererMountContext> MountContexts { get; } = new();
        public bool ThrowOnMount { get; set; }

        public Task<LayoutRendererPackage?> GetRendererPackageAsync(IVersoContext context)
            => Task.FromResult<LayoutRendererPackage?>(null);
        public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
            => Task.FromResult(new RenderResult("text/html", ""));
        public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
            => Task.FromResult(new CellContainerInfo(cellId, 0, 0, 0, 0));
        public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
        public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
        public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;
        public Dictionary<string, object> GetLayoutMetadata() => new();
        public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context) => Task.CompletedTask;

        public Task<IDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
        {
            MountContexts.Add(context);
            if (ThrowOnMount)
                throw new InvalidOperationException("mount boom");
            return Task.FromResult(_initPayload);
        }

        public Task OnRendererUnmountedAsync(LayoutRendererUnmountContext context) => Task.CompletedTask;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
    }
}
