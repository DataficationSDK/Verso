using Verso.Abstractions;
using Verso.Extensions;

namespace Verso.Tests.Extensions;

[TestClass]
public class PanelRegistrationTests
{
    private ExtensionHost _host = null!;

    [TestInitialize]
    public void Setup() => _host = new ExtensionHost();

    [TestCleanup]
    public async Task Cleanup() => await _host.DisposeAsync();

    [TestMethod]
    public async Task LoadExtension_Panel_IsDiscoverableByPair()
    {
        var panel = new TestPanel("com.test.panels", "findings");

        await _host.LoadExtensionAsync(panel);

        Assert.IsTrue(_host.TryGetPanel("com.test.panels", "findings", out var found));
        Assert.AreSame(panel, found);
        CollectionAssert.Contains(_host.GetPanels().ToList(), panel);
    }

    [TestMethod]
    public async Task TryGetPanel_IsCaseInsensitive()
    {
        await _host.LoadExtensionAsync(new TestPanel("com.test.panels", "findings"));

        Assert.IsTrue(_host.TryGetPanel("COM.TEST.PANELS", "FINDINGS", out _));
    }

    [TestMethod]
    public async Task TryGetPanel_WrongExtension_DoesNotMatch()
    {
        // The identity is the pair, so a panel id alone must never resolve. Two
        // extensions are free to use the same panel id.
        await _host.LoadExtensionAsync(new TestPanel("com.test.first", "findings"));
        await _host.LoadExtensionAsync(new TestPanel("com.test.second", "findings"));

        Assert.IsTrue(_host.TryGetPanel("com.test.first", "findings", out var first));
        Assert.IsTrue(_host.TryGetPanel("com.test.second", "findings", out var second));
        Assert.AreNotSame(first, second);
        Assert.IsFalse(_host.TryGetPanel("com.test.third", "findings", out _));
    }

    [TestMethod]
    public async Task LoadExtension_DuplicatePanelIdInSameExtension_IsRejected()
    {
        await _host.LoadExtensionAsync(new TestPanel("com.test.dup", "findings"));

        // A separate type so the duplicate-extension-id check does not fire first.
        var second = new SecondTestPanel("com.test.dup", "findings");

        var ex = await Assert.ThrowsExceptionAsync<ExtensionLoadException>(
            () => _host.LoadExtensionAsync(second));

        Assert.IsTrue(ex.Errors.Any(e => e.ErrorCode == "PANEL_ID_DUPLICATE_IN_EXTENSION"),
            "Expected PANEL_ID_DUPLICATE_IN_EXTENSION. Got: " +
            string.Join(", ", ex.Errors.Select(e => e.ErrorCode)));
    }

    [TestMethod]
    public async Task LoadExtension_PanelAndHandlerOnOneClass_IndexesBoth()
    {
        var panel = new TestPanelWithHandler("com.test.interactive", "findings");

        await _host.LoadExtensionAsync(panel);

        Assert.IsTrue(_host.TryGetPanelInteractionHandler(
            "com.test.interactive", "findings", out var handler));
        Assert.AreSame(panel, handler);
    }

    [TestMethod]
    public async Task LoadExtension_OrphanHandler_IsRejected()
    {
        // A handler whose extension registers no panel with that id has nothing to
        // service, which is a load-time error rather than a silent no-op.
        var orphan = new OrphanPanelHandler("com.test.orphan", "phantom");

        var ex = await Assert.ThrowsExceptionAsync<ExtensionLoadException>(
            () => _host.LoadExtensionAsync(orphan));

        Assert.IsTrue(ex.Errors.Any(e => e.ErrorCode == "PANEL_HANDLER_ORPHANED"),
            "Expected PANEL_HANDLER_ORPHANED. Got: " +
            string.Join(", ", ex.Errors.Select(e => e.ErrorCode)));
    }

    [TestMethod]
    public async Task LoadExtension_DuplicateHandlerForSamePanel_IsRejected()
    {
        await _host.LoadExtensionAsync(new TestPanelWithHandler("com.test.duphandler", "findings"));

        // A second handler for the same (ExtensionId, PanelId). Loading also trips the
        // unique-extension-id rule, so this asserts only that the panel-specific
        // diagnostic is among the reported errors.
        var second = new OrphanPanelHandler("com.test.duphandler", "findings");

        var ex = await Assert.ThrowsExceptionAsync<ExtensionLoadException>(
            () => _host.LoadExtensionAsync(second));

        Assert.IsTrue(ex.Errors.Any(e => e.ErrorCode == "PANEL_INTERACTION_DUPLICATE"),
            "Expected PANEL_INTERACTION_DUPLICATE. Got: " +
            string.Join(", ", ex.Errors.Select(e => e.ErrorCode)));
    }

    [TestMethod]
    public async Task GetPanels_ExcludesDisabledExtensions()
    {
        var panel = new TestPanel("com.test.disabled", "findings");
        await _host.LoadExtensionAsync(panel);

        await _host.DisableExtensionAsync("com.test.disabled");

        Assert.AreEqual(0, _host.GetPanels().Count);
        Assert.IsFalse(_host.TryGetPanel("com.test.disabled", "findings", out _));
    }

    // ── Fakes ──────────────────────────────────────────────────────────

    private class TestPanel : INotebookPanel
    {
        public TestPanel(string extensionId, string panelId)
        {
            ExtensionId = extensionId;
            PanelId = panelId;
        }

        public string ExtensionId { get; }
        public string Name => "Test Panel";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;

        public string PanelId { get; }
        public string DisplayName => "Test";
        public string? IconName => "list";
        public int Order => 600;

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;

        public Task<bool> IsAvailableAsync(IPanelContext context) => Task.FromResult(true);

        public Task<IReadOnlyList<RenderResult>> RenderAsync(IPanelContext context)
            => Task.FromResult<IReadOnlyList<RenderResult>>(
                new[] { new RenderResult("text/plain", "test") });
    }

    private sealed class SecondTestPanel : TestPanel
    {
        public SecondTestPanel(string extensionId, string panelId)
            : base(extensionId, panelId) { }
    }

    private sealed class TestPanelWithHandler : TestPanel, IPanelInteractionHandler
    {
        public TestPanelWithHandler(string extensionId, string panelId)
            : base(extensionId, panelId) { }

        public Task OnPanelInteractionAsync(PanelInteractionContext context)
            => Task.CompletedTask;
    }

    private sealed class OrphanPanelHandler : IPanelInteractionHandler
    {
        public OrphanPanelHandler(string extensionId, string panelId)
        {
            ExtensionId = extensionId;
            PanelId = panelId;
        }

        public string ExtensionId { get; }
        public string Name => "Orphan Handler";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;

        public string PanelId { get; }

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;

        public Task OnPanelInteractionAsync(PanelInteractionContext context)
            => Task.CompletedTask;
    }
}
