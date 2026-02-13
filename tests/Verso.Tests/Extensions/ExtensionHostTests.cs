using Verso.Abstractions;
using Verso.Extensions;
using Verso.Tests.Helpers;

namespace Verso.Tests.Extensions;

[TestClass]
public class ExtensionHostTests
{
    private ExtensionHost _host = null!;

    [TestInitialize]
    public void Setup() => _host = new ExtensionHost();

    [TestCleanup]
    public async Task Cleanup() => await _host.DisposeAsync();

    // --- Lifecycle: OnLoadedAsync called ---

    [TestMethod]
    public async Task LoadExtension_CallsOnLoadedAsync()
    {
        var kernel = new FakeLanguageKernel(languageId: "lc");
        await _host.LoadExtensionAsync(kernel);

        // FakeLanguageKernel doesn't track OnLoaded, but we can verify it's in the list
        var loaded = _host.GetLoadedExtensions();
        Assert.AreEqual(1, loaded.Count);
        Assert.AreSame(kernel, loaded[0]);
    }

    [TestMethod]
    public async Task LoadExtension_FakeExtensionTracksOnLoadedCalls()
    {
        var renderer = new FakeCellRenderer();
        await _host.LoadExtensionAsync(renderer);

        Assert.AreEqual(1, renderer.OnLoadedCallCount);
    }

    // --- Auto-registration by interface type ---

    [TestMethod]
    public async Task LoadExtension_Kernel_RegisteredInKernelsList()
    {
        var kernel = new FakeLanguageKernel(languageId: "k1");
        await _host.LoadExtensionAsync(kernel);

        var kernels = _host.GetKernels();
        Assert.AreEqual(1, kernels.Count);
        Assert.AreSame(kernel, kernels[0]);
    }

    [TestMethod]
    public async Task LoadExtension_Renderer_RegisteredInRenderersList()
    {
        var renderer = new FakeCellRenderer();
        await _host.LoadExtensionAsync(renderer);

        var renderers = _host.GetRenderers();
        Assert.AreEqual(1, renderers.Count);
        Assert.AreSame(renderer, renderers[0]);
    }

    [TestMethod]
    public async Task LoadExtension_Formatter_RegisteredInFormattersList()
    {
        var formatter = new FakeDataFormatter();
        await _host.LoadExtensionAsync(formatter);

        var formatters = _host.GetFormatters();
        Assert.AreEqual(1, formatters.Count);
        Assert.AreSame(formatter, formatters[0]);
    }

    // --- Built-in discovery (CSharpKernel) ---

    [TestMethod]
    public async Task LoadBuiltInExtensions_DiscoversCSharpKernel()
    {
        await _host.LoadBuiltInExtensionsAsync();

        var kernels = _host.GetKernels();
        Assert.IsTrue(kernels.Any(k => k.LanguageId == "csharp"),
            "CSharpKernel should be discovered as a built-in extension.");
    }

    [TestMethod]
    public async Task LoadBuiltInExtensions_CSharpKernelInLoadedExtensions()
    {
        await _host.LoadBuiltInExtensionsAsync();

        var loaded = _host.GetLoadedExtensions();
        Assert.IsTrue(loaded.Any(e => e.ExtensionId == "verso.kernel.csharp"));
    }

    // --- Snapshot isolation ---

    [TestMethod]
    public async Task GetLoadedExtensions_ReturnsSnapshot()
    {
        var kernel = new FakeLanguageKernel(languageId: "snap");
        await _host.LoadExtensionAsync(kernel);

        var snapshot = _host.GetLoadedExtensions();
        // Loading another extension shouldn't affect the existing snapshot
        var kernel2 = new FakeLanguageKernel(languageId: "snap2");
        await _host.LoadExtensionAsync(kernel2);

        Assert.AreEqual(1, snapshot.Count, "Original snapshot should not be mutated.");
        Assert.AreEqual(2, _host.GetLoadedExtensions().Count);
    }

    [TestMethod]
    public async Task GetKernels_ReturnsSnapshot()
    {
        var kernel = new FakeLanguageKernel(languageId: "ksnap");
        await _host.LoadExtensionAsync(kernel);

        var snapshot = _host.GetKernels();
        var kernel2 = new FakeLanguageKernel(languageId: "ksnap2");
        await _host.LoadExtensionAsync(kernel2);

        Assert.AreEqual(1, snapshot.Count);
    }

    // --- Duplicate rejection ---

    [TestMethod]
    public async Task LoadExtension_DuplicateId_Throws()
    {
        var first = new FakeLanguageKernel(languageId: "d1");
        await _host.LoadExtensionAsync(first);

        var second = new FakeLanguageKernel(languageId: "d1"); // same ExtensionId pattern
        await Assert.ThrowsExceptionAsync<ExtensionLoadException>(
            () => _host.LoadExtensionAsync(second));
    }

    // --- Dispose / Unload ---

    [TestMethod]
    public async Task DisposeAsync_ClearsAllExtensions()
    {
        var kernel = new FakeLanguageKernel(languageId: "disp");
        await _host.LoadExtensionAsync(kernel);

        await _host.DisposeAsync();

        Assert.AreEqual(0, _host.GetLoadedExtensions().Count);
        Assert.AreEqual(0, _host.GetKernels().Count);
    }

    [TestMethod]
    public async Task UnloadAll_CallsOnUnloadedAsync()
    {
        var renderer = new FakeCellRenderer();
        await _host.LoadExtensionAsync(renderer);

        await _host.UnloadAllAsync();

        Assert.AreEqual(1, renderer.OnUnloadedCallCount);
    }

    [TestMethod]
    public async Task UnloadAll_DisposesAsyncDisposableExtensions()
    {
        var kernel = new FakeLanguageKernel(languageId: "udisp");
        await _host.LoadExtensionAsync(kernel);

        await _host.UnloadAllAsync();

        Assert.AreEqual(1, kernel.DisposeCallCount);
    }

    // --- Unload reverse order ---

    [TestMethod]
    public async Task UnloadAll_CallsOnUnloadedInReverseOrder()
    {
        var unloadOrder = new List<string>();

        var first = new TrackingKernel("first", unloadOrder);
        var second = new TrackingKernel("second", unloadOrder);
        var third = new TrackingKernel("third", unloadOrder);

        await _host.LoadExtensionAsync(first);
        await _host.LoadExtensionAsync(second);
        await _host.LoadExtensionAsync(third);

        await _host.UnloadAllAsync();

        CollectionAssert.AreEqual(
            new[] { "third", "second", "first" },
            unloadOrder);
    }

    // --- Directory / Assembly not-found errors ---

    [TestMethod]
    public async Task LoadFromDirectory_NotFound_Throws()
    {
        await Assert.ThrowsExceptionAsync<DirectoryNotFoundException>(
            () => _host.LoadFromDirectoryAsync("/nonexistent/path/12345"));
    }

    [TestMethod]
    public async Task LoadFromAssembly_NotFound_Throws()
    {
        await Assert.ThrowsExceptionAsync<FileNotFoundException>(
            () => _host.LoadFromAssemblyAsync("/nonexistent/assembly.dll"));
    }

    // --- Disposed host throws ---

    [TestMethod]
    public async Task LoadExtension_AfterDispose_Throws()
    {
        await _host.DisposeAsync();

        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(
            () => _host.LoadExtensionAsync(new FakeLanguageKernel(languageId: "post")));
    }

    [TestMethod]
    public async Task LoadBuiltIn_AfterDispose_Throws()
    {
        await _host.DisposeAsync();

        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(
            () => _host.LoadBuiltInExtensionsAsync());
    }

    // --- Multiple category registration ---

    [TestMethod]
    public async Task LoadExtension_ExtensionInBothLoadedAndCategoryList()
    {
        var formatter = new FakeDataFormatter();
        await _host.LoadExtensionAsync(formatter);

        Assert.AreEqual(1, _host.GetLoadedExtensions().Count);
        Assert.AreEqual(1, _host.GetFormatters().Count);
        Assert.AreEqual(0, _host.GetKernels().Count);
        Assert.AreEqual(0, _host.GetRenderers().Count);
    }

    // --- Helper: kernel that tracks unload order ---

    private sealed class TrackingKernel : ILanguageKernel
    {
        private readonly string _id;
        private readonly List<string> _unloadOrder;

        public TrackingKernel(string id, List<string> unloadOrder)
        {
            _id = id;
            _unloadOrder = unloadOrder;
        }

        public string ExtensionId => $"com.test.track.{_id}";
        public string Name => _id;
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public string LanguageId => _id;
        public string DisplayName => _id;
        public IReadOnlyList<string> FileExtensions => Array.Empty<string>();

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;

        public Task OnUnloadedAsync()
        {
            _unloadOrder.Add(_id);
            return Task.CompletedTask;
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
            => Task.FromResult<IReadOnlyList<CellOutput>>(Array.Empty<CellOutput>());
        public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
            => Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());
        public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
            => Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());
        public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
            => Task.FromResult<HoverInfo?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
