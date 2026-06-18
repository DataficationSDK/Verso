using Verso.Abstractions;
using Verso.Extensions;

namespace Verso.Tests.Extensions;

[TestClass]
public class LayoutLifecycleRegistrationTests
{
    private ExtensionHost _host = null!;

    [TestInitialize]
    public void Setup() => _host = new ExtensionHost();

    [TestCleanup]
    public async Task Cleanup() => await _host.DisposeAsync();

    [TestMethod]
    public async Task LoadExtension_ValidEngineAndHandler_IndexedByPair()
    {
        var handler = new EngineAndLifecycle(
            extensionId: "com.test.layout.tier2",
            layoutId: "sparkline");

        await _host.LoadExtensionAsync(handler);

        Assert.IsTrue(_host.TryGetLayoutLifecycleHandler(
            "com.test.layout.tier2", "sparkline", out var found));
        Assert.AreSame(handler, found);
    }

    [TestMethod]
    public void LoadExtension_OrphanHandler_EmitsLayoutHandlerOrphaned()
    {
        var orphan = new OrphanLifecycleHandler(
            extensionId: "com.test.layout.orphan",
            layoutId: "phantom");

        var ex = Assert.ThrowsException<ExtensionLoadException>(
            () => _host.LoadExtensionAsync(orphan).GetAwaiter().GetResult());

        Assert.IsTrue(ex.Errors.Any(e => e.ErrorCode == "LAYOUT_HANDLER_ORPHANED"),
            "Expected LAYOUT_HANDLER_ORPHANED diagnostic. Got: " +
            string.Join(", ", ex.Errors.Select(e => e.ErrorCode)));
    }

    [TestMethod]
    public async Task LoadExtension_DuplicateLifecycleSameExtension_EmitsDuplicate()
    {
        var first = new EngineAndLifecycle(
            extensionId: "com.test.layout.dup",
            layoutId: "sparkline");
        await _host.LoadExtensionAsync(first);

        var second = new SecondaryLifecycleHandler(
            extensionId: first.ExtensionId,
            layoutId: first.LayoutId);

        var ex = Assert.ThrowsException<ExtensionLoadException>(
            () => _host.LoadExtensionAsync(second).GetAwaiter().GetResult());

        Assert.IsTrue(ex.Errors.Any(e => e.ErrorCode == "LAYOUT_LIFECYCLE_DUPLICATE"),
            "Expected LAYOUT_LIFECYCLE_DUPLICATE diagnostic. Got: " +
            string.Join(", ", ex.Errors.Select(e => e.ErrorCode)));
    }

    [TestMethod]
    public async Task LoadExtension_TwoExtensionsSameLayoutId_BothLoad()
    {
        var a = new EngineAndLifecycle(
            extensionId: "com.contoso.viz",
            layoutId: "sparkline");
        var b = new EngineAndLifecycle(
            extensionId: "com.fabrikam.viz",
            layoutId: "sparkline");

        await _host.LoadExtensionAsync(a);
        await _host.LoadExtensionAsync(b);

        Assert.IsTrue(_host.TryGetLayoutLifecycleHandler("com.contoso.viz", "sparkline", out var foundA));
        Assert.IsTrue(_host.TryGetLayoutLifecycleHandler("com.fabrikam.viz", "sparkline", out var foundB));
        Assert.AreSame(a, foundA);
        Assert.AreSame(b, foundB);
    }

    [TestMethod]
    public async Task TryGetLayoutLifecycleHandler_DisabledExtension_ReturnsFalse()
    {
        var handler = new EngineAndLifecycle(
            extensionId: "com.test.layout.disabled",
            layoutId: "sparkline");
        await _host.LoadExtensionAsync(handler);

        await _host.DisableExtensionAsync(handler.ExtensionId);

        Assert.IsFalse(_host.TryGetLayoutLifecycleHandler(
            handler.ExtensionId, handler.LayoutId, out _));
    }

    [TestMethod]
    public async Task TryGetLayoutLifecycleHandler_CaseInsensitiveLookup()
    {
        var handler = new EngineAndLifecycle(
            extensionId: "com.test.layout.case",
            layoutId: "Sparkline");
        await _host.LoadExtensionAsync(handler);

        Assert.IsTrue(_host.TryGetLayoutLifecycleHandler(
            "COM.TEST.LAYOUT.CASE", "sparkline", out var found));
        Assert.AreSame(handler, found);
    }

    private sealed class OrphanLifecycleHandler : IExtension, ILayoutLifecycleHandler
    {
        public OrphanLifecycleHandler(string extensionId, string layoutId)
        {
            ExtensionId = extensionId;
            LayoutId = layoutId;
        }

        public string ExtensionId { get; }
        public string Name => "Orphan Lifecycle";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public string LayoutId { get; }

        public Task<IReadOnlyDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
            => Task.FromResult<IReadOnlyDictionary<string, object>?>(null);
        public Task OnRendererUnmountedAsync(LayoutRendererUnmountContext context) => Task.CompletedTask;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
    }

    private sealed class EngineAndLifecycle : IExtension, ILayoutEngine, ILayoutLifecycleHandler
    {
        public EngineAndLifecycle(string extensionId, string layoutId)
        {
            ExtensionId = extensionId;
            LayoutId = layoutId;
        }

        public string ExtensionId { get; }
        public string Name => "Engine And Lifecycle";
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

        public Task<IReadOnlyDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
            => Task.FromResult<IReadOnlyDictionary<string, object>?>(null);
        public Task OnRendererUnmountedAsync(LayoutRendererUnmountContext context) => Task.CompletedTask;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
    }

    private sealed class SecondaryLifecycleHandler : IExtension, ILayoutEngine, ILayoutLifecycleHandler
    {
        public SecondaryLifecycleHandler(string extensionId, string layoutId)
        {
            ExtensionId = extensionId;
            LayoutId = layoutId;
        }

        public string ExtensionId { get; }
        public string Name => "Secondary Lifecycle";
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

        public Task<IReadOnlyDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
            => Task.FromResult<IReadOnlyDictionary<string, object>?>(null);
        public Task OnRendererUnmountedAsync(LayoutRendererUnmountContext context) => Task.CompletedTask;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
    }

}
