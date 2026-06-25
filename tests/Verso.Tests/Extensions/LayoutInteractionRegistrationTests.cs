using Verso.Abstractions;
using Verso.Extensions;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public class LayoutInteractionRegistrationTests
{
    private ExtensionHost _host = null!;

    [TestInitialize]
    public void Setup() => _host = new ExtensionHost();

    [TestCleanup]
    public async Task Cleanup() => await _host.DisposeAsync();

    [TestMethod]
    public async Task LoadExtension_ValidEngineAndHandler_IndexedByPair()
    {
        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.dashboard",
            layoutId: "dashboard");

        await _host.LoadExtensionAsync(handler);

        Assert.IsTrue(_host.TryGetLayoutInteractionHandler(
            "com.test.layout.dashboard", "dashboard", out var found));
        Assert.AreSame(handler, found);
    }

    [TestMethod]
    public void LoadExtension_OrphanHandler_EmitsLayoutHandlerOrphaned()
    {
        // A handler-only extension that does NOT implement ILayoutEngine.
        var orphan = new OrphanLayoutInteractionHandler(
            extensionId: "com.test.layout.orphan",
            layoutId: "phantom");

        var ex = Assert.ThrowsException<ExtensionLoadException>(
            () => _host.LoadExtensionAsync(orphan).GetAwaiter().GetResult());

        Assert.IsTrue(ex.Errors.Any(e => e.ErrorCode == "LAYOUT_HANDLER_ORPHANED"),
            "Expected LAYOUT_HANDLER_ORPHANED diagnostic. Got: " +
            string.Join(", ", ex.Errors.Select(e => e.ErrorCode)));
    }

    [TestMethod]
    public async Task LoadExtension_DuplicateHandlerSameExtension_EmitsDuplicate()
    {
        var first = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.dup",
            layoutId: "dashboard",
            name: "First");
        await _host.LoadExtensionAsync(first);

        // A second handler in the SAME extension targeting the SAME LayoutId.
        // We construct it as a separate type so the DUPLICATE_ID extension check
        // does not fire first.
        var second = new SecondaryLayoutHandler(
            extensionId: first.ExtensionId,
            layoutId: first.LayoutId);

        // The host treats this as a duplicate (ExtensionId, LayoutId) pair even
        // though the IExtension.ExtensionId itself is also "duplicate"; what we
        // verify is the LAYOUT_INTERACTION_DUPLICATE code is present.
        var ex = Assert.ThrowsException<ExtensionLoadException>(
            () => _host.LoadExtensionAsync(second).GetAwaiter().GetResult());

        Assert.IsTrue(ex.Errors.Any(e => e.ErrorCode == "LAYOUT_INTERACTION_DUPLICATE"),
            "Expected LAYOUT_INTERACTION_DUPLICATE diagnostic. Got: " +
            string.Join(", ", ex.Errors.Select(e => e.ErrorCode)));
    }

    [TestMethod]
    public async Task LoadExtension_TwoExtensionsSameLayoutId_BothLoad()
    {
        // Two different extensions registering layouts with the same LayoutId
        // must both load and coexist under their distinct ExtensionIds.
        var a = new FakeLayoutInteractionHandler(
            extensionId: "com.contoso.viz",
            layoutId: "grid");
        var b = new FakeLayoutInteractionHandler(
            extensionId: "com.fabrikam.viz",
            layoutId: "grid");

        await _host.LoadExtensionAsync(a);
        await _host.LoadExtensionAsync(b);

        Assert.IsTrue(_host.TryGetLayoutInteractionHandler("com.contoso.viz", "grid", out var foundA));
        Assert.IsTrue(_host.TryGetLayoutInteractionHandler("com.fabrikam.viz", "grid", out var foundB));
        Assert.AreSame(a, foundA);
        Assert.AreSame(b, foundB);
    }

    [TestMethod]
    public async Task TryGetLayoutInteractionHandler_DisabledExtension_ReturnsFalse()
    {
        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.disabled",
            layoutId: "dashboard");
        await _host.LoadExtensionAsync(handler);

        await _host.DisableExtensionAsync(handler.ExtensionId);

        Assert.IsFalse(_host.TryGetLayoutInteractionHandler(
            handler.ExtensionId, handler.LayoutId, out _));
    }

    [TestMethod]
    public async Task TryGetLayoutInteractionHandler_UnknownPair_ReturnsFalse()
    {
        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.known",
            layoutId: "dashboard");
        await _host.LoadExtensionAsync(handler);

        Assert.IsFalse(_host.TryGetLayoutInteractionHandler(
            "com.test.layout.known", "presentation", out _));
        Assert.IsFalse(_host.TryGetLayoutInteractionHandler(
            "com.test.layout.unknown", "dashboard", out _));
    }

    [TestMethod]
    public async Task TryGetLayoutInteractionHandler_CaseInsensitiveLookup()
    {
        var handler = new FakeLayoutInteractionHandler(
            extensionId: "com.test.layout.case",
            layoutId: "Dashboard");
        await _host.LoadExtensionAsync(handler);

        Assert.IsTrue(_host.TryGetLayoutInteractionHandler(
            "COM.TEST.LAYOUT.CASE", "dashboard", out var found));
        Assert.AreSame(handler, found);
    }

    /// <summary>
    /// A handler-only extension with no <see cref="ILayoutEngine"/> capability,
    /// used to exercise the orphan diagnostic.
    /// </summary>
    private sealed class OrphanLayoutInteractionHandler : IExtension, ILayoutInteractionHandler
    {
        public OrphanLayoutInteractionHandler(string extensionId, string layoutId)
        {
            ExtensionId = extensionId;
            LayoutId = layoutId;
        }

        public string ExtensionId { get; }
        public string Name => "Orphan Handler";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public string LayoutId { get; }

        public Task OnLayoutInteractionAsync(LayoutInteractionContext context) => Task.CompletedTask;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// A second handler class with the same ExtensionId as a previously-loaded
    /// FakeLayoutInteractionHandler. Used to exercise the duplicate diagnostic;
    /// this type also implements ILayoutEngine so the orphan check passes.
    /// </summary>
    private sealed class SecondaryLayoutHandler : IExtension, ILayoutEngine, ILayoutInteractionHandler
    {
        public SecondaryLayoutHandler(string extensionId, string layoutId)
        {
            ExtensionId = extensionId;
            LayoutId = layoutId;
        }

        public string ExtensionId { get; }
        public string Name => "Secondary Handler";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public string LayoutId { get; }
        public string DisplayName => Name;
        public string? Icon => null;
        public LayoutCapabilities Capabilities => LayoutCapabilities.None;
        public bool RequiresCustomRenderer => false;

        public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
            => Task.FromResult(new RenderResult("text/html", ""));
        public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
            => Task.FromResult(new CellContainerInfo(cellId, 0, 0, 0, 0));
        public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
        public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
        public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;
        public Dictionary<string, object> GetLayoutMetadata() => new();
        public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context) => Task.CompletedTask;

        public Task OnLayoutInteractionAsync(LayoutInteractionContext context) => Task.CompletedTask;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
    }
}
