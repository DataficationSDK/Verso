using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Extensions;
using Verso.Host.Layouts;
using Verso.Testing.Stubs;

namespace Verso.Host.Tests.Layouts;

[TestClass]
public class LayoutSessionFrameTrackerTests
{
    private ExtensionHost _host = null!;

    [TestInitialize]
    public void Setup() => _host = new ExtensionHost();

    [TestCleanup]
    public async Task Cleanup() => await _host.DisposeAsync();

    [TestMethod]
    public void AllocateFrameInstanceId_FormatIsNotebookSlashLayoutSlashSeq()
    {
        var tracker = NewTracker(notebookId: "nb-7");

        var first = tracker.AllocateFrameInstanceId("sparkline");
        var second = tracker.AllocateFrameInstanceId("sparkline");

        Assert.AreEqual("nb-7/sparkline/1", first);
        Assert.AreEqual("nb-7/sparkline/2", second);
    }

    [TestMethod]
    public async Task InvokeMountedAsync_DispatchesContextAndReturnsHandlerPayload()
    {
        var lifecycle = new RecordingLifecycleExtension(
            extensionId: "com.test.tracker.mount",
            layoutId: "sparkline",
            initPayload: new Dictionary<string, object> { ["series"] = "demo" });
        await _host.LoadExtensionAsync(lifecycle);

        var tracker = NewTracker(notebookId: "nb-1");
        var channel = new RecordingChannel("nb-1/sparkline/1");

        var result = await tracker.InvokeMountedAsync(
            extensionId: "com.test.tracker.mount",
            layoutId: "sparkline",
            frameInstanceId: "nb-1/sparkline/1",
            isolation: LayoutRendererIsolation.Isolated,
            channel: channel);

        Assert.IsNotNull(result);
        Assert.AreEqual("demo", result!["series"]);
        Assert.AreEqual(1, lifecycle.MountContexts.Count);

        var ctx = lifecycle.MountContexts[0];
        Assert.AreEqual("com.test.tracker.mount", ctx.ExtensionId);
        Assert.AreEqual("sparkline", ctx.LayoutId);
        Assert.AreEqual("nb-1/sparkline/1", ctx.FrameInstanceId);
        Assert.AreEqual(LayoutRendererIsolation.Isolated, ctx.Isolation);
        Assert.IsNotNull(ctx.Verso);
        Assert.AreSame(channel, ctx.Frame);
    }

    [TestMethod]
    public async Task InvokeMountedAsync_NoHandlerRegistered_ReturnsNullButStillTracks()
    {
        // Engine present but no lifecycle handler — mount should still record the
        // frame so a later DisposeAsync teardown is a no-op rather than a leak.
        var engine = new EngineOnlyExtension(
            extensionId: "com.test.tracker.nohandler",
            layoutId: "sparkline");
        await _host.LoadExtensionAsync(engine);

        var tracker = NewTracker(notebookId: "nb-2");
        var channel = new RecordingChannel("nb-2/sparkline/1");

        var result = await tracker.InvokeMountedAsync(
            extensionId: "com.test.tracker.nohandler",
            layoutId: "sparkline",
            frameInstanceId: "nb-2/sparkline/1",
            isolation: LayoutRendererIsolation.Isolated,
            channel: channel);

        Assert.IsNull(result);

        // DisposeAsync should complete without invoking any handler.
        await tracker.DisposeAsync();
        Assert.IsFalse(channel.IsAlive, "Channel should have been marked dead during dispose teardown.");
    }

    [TestMethod]
    public async Task InvokeUnmountedAsync_MarksChannelDeadBeforeHandlerRuns()
    {
        var lifecycle = new RecordingLifecycleExtension(
            extensionId: "com.test.tracker.unmount",
            layoutId: "sparkline");
        await _host.LoadExtensionAsync(lifecycle);

        var tracker = NewTracker(notebookId: "nb-3");
        var channel = new RecordingChannel("nb-3/sparkline/1");

        await tracker.InvokeMountedAsync(
            extensionId: "com.test.tracker.unmount",
            layoutId: "sparkline",
            frameInstanceId: "nb-3/sparkline/1",
            isolation: LayoutRendererIsolation.Isolated,
            channel: channel);

        lifecycle.CaptureChannelStateOnUnmount = () => channel.IsAlive;

        await tracker.InvokeUnmountedAsync(
            extensionId: "com.test.tracker.unmount",
            layoutId: "sparkline",
            frameInstanceId: "nb-3/sparkline/1",
            isolation: LayoutRendererIsolation.Isolated);

        Assert.AreEqual(1, lifecycle.UnmountContexts.Count);
        Assert.AreEqual(false, lifecycle.IsAliveObservedDuringUnmount,
            "Frame.IsAlive must be false by the time OnRendererUnmountedAsync runs.");
        Assert.IsFalse(channel.IsAlive);
    }

    [TestMethod]
    public async Task DisposeAsync_UnmountsEveryActiveFrame()
    {
        var lifecycle = new RecordingLifecycleExtension(
            extensionId: "com.test.tracker.dispose",
            layoutId: "sparkline");
        await _host.LoadExtensionAsync(lifecycle);

        var tracker = NewTracker(notebookId: "nb-4");
        var ch1 = new RecordingChannel("nb-4/sparkline/1");
        var ch2 = new RecordingChannel("nb-4/sparkline/2");

        await tracker.InvokeMountedAsync("com.test.tracker.dispose", "sparkline",
            "nb-4/sparkline/1", LayoutRendererIsolation.Isolated, ch1);
        await tracker.InvokeMountedAsync("com.test.tracker.dispose", "sparkline",
            "nb-4/sparkline/2", LayoutRendererIsolation.Isolated, ch2);

        await tracker.DisposeAsync();

        Assert.AreEqual(2, lifecycle.UnmountContexts.Count);
        Assert.IsFalse(ch1.IsAlive);
        Assert.IsFalse(ch2.IsAlive);
    }

    [TestMethod]
    public async Task DisposeAsync_HandlerThrows_OtherFramesStillUnmount()
    {
        var lifecycle = new RecordingLifecycleExtension(
            extensionId: "com.test.tracker.throws",
            layoutId: "sparkline")
        {
            ThrowOnUnmount = true
        };
        await _host.LoadExtensionAsync(lifecycle);

        var tracker = NewTracker(notebookId: "nb-5");
        var ch1 = new RecordingChannel("nb-5/sparkline/1");
        var ch2 = new RecordingChannel("nb-5/sparkline/2");

        await tracker.InvokeMountedAsync("com.test.tracker.throws", "sparkline",
            "nb-5/sparkline/1", LayoutRendererIsolation.Isolated, ch1);
        await tracker.InvokeMountedAsync("com.test.tracker.throws", "sparkline",
            "nb-5/sparkline/2", LayoutRendererIsolation.Isolated, ch2);

        // Dispose must swallow handler exceptions so the session teardown is not blocked.
        await tracker.DisposeAsync();

        Assert.IsFalse(ch1.IsAlive);
        Assert.IsFalse(ch2.IsAlive);
    }

    private LayoutSessionFrameTracker NewTracker(string notebookId)
        => new(notebookId, _host, () => new StubVersoContext());

    private sealed class RecordingChannel : LayoutFrameChannel
    {
        public RecordingChannel(string frameInstanceId) : base(frameInstanceId) { }

        public List<(string Type, object? Payload)> Posted { get; } = new();

        protected override Task PostMessageCoreAsync(string type, object? payload, CancellationToken ct)
        {
            Posted.Add((type, payload));
            return Task.CompletedTask;
        }
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

        public List<LayoutRendererMountContext> MountContexts { get; } = new();
        public List<LayoutRendererUnmountContext> UnmountContexts { get; } = new();
        public Func<bool>? CaptureChannelStateOnUnmount { get; set; }
        public bool? IsAliveObservedDuringUnmount { get; private set; }
        public bool ThrowOnUnmount { get; set; }

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
            return Task.FromResult(_initPayload);
        }

        public Task OnRendererUnmountedAsync(LayoutRendererUnmountContext context)
        {
            UnmountContexts.Add(context);
            if (CaptureChannelStateOnUnmount is not null)
                IsAliveObservedDuringUnmount = CaptureChannelStateOnUnmount();
            if (ThrowOnUnmount)
                throw new InvalidOperationException("unmount failure (test)");
            return Task.CompletedTask;
        }

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
    }

    private sealed class EngineOnlyExtension : IExtension, ILayoutEngine
    {
        public EngineOnlyExtension(string extensionId, string layoutId)
        {
            ExtensionId = extensionId;
            LayoutId = layoutId;
        }

        public string ExtensionId { get; }
        public string Name => "Engine Only";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public string LayoutId { get; }
        public string DisplayName => Name;
        public string? Icon => null;
        public LayoutCapabilities Capabilities => LayoutCapabilities.None;
        public bool RequiresCustomRenderer => true;

        public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
            => Task.FromResult(new RenderResult("text/html", ""));
        public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
            => Task.FromResult(new CellContainerInfo(cellId, 0, 0, 0, 0));
        public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
        public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
        public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;
        public Dictionary<string, object> GetLayoutMetadata() => new();
        public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context) => Task.CompletedTask;

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
    }
}
