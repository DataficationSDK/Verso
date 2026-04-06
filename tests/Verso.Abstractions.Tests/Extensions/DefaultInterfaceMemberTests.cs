namespace Verso.Abstractions.Tests.Extensions;

[TestClass]
public class DefaultInterfaceMemberTests
{
    [TestMethod]
    public void ICellRenderer_DefaultVisibility_ReturnsContent()
    {
        ICellRenderer renderer = new StubCellRenderer();
        Assert.AreEqual(CellVisibilityHint.Content, renderer.DefaultVisibility);
    }

    [TestMethod]
    public void ILayoutEngine_SupportedVisibilityStates_ContainsOnlyVisible()
    {
        ILayoutEngine layout = new StubLayoutEngine();
        var states = layout.SupportedVisibilityStates;

        Assert.AreEqual(1, states.Count);
        Assert.IsTrue(states.Contains(CellVisibilityState.Visible));
    }

    [TestMethod]
    public void ILayoutEngine_SupportsPropertiesPanel_ReturnsFalse()
    {
        ILayoutEngine layout = new StubLayoutEngine();
        Assert.IsFalse(layout.SupportsPropertiesPanel);
    }

    [TestMethod]
    public void IExtensionHostContext_GetPropertyProviders_ReturnsEmptyList()
    {
        IExtensionHostContext context = new StubExtensionHostContext();
        var providers = context.GetPropertyProviders();

        Assert.IsNotNull(providers);
        Assert.AreEqual(0, providers.Count);
    }

    #region Minimal stubs (implement only required abstract members)

    private class StubCellRenderer : ICellRenderer
    {
        public string CellTypeId => "stub";
        public string DisplayName => "Stub";
        public string ExtensionId => "test.stub.renderer";
        public string Name => "Stub Renderer";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
        public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context) => throw new NotImplementedException();
        public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context) => throw new NotImplementedException();
        public string? GetEditorLanguage() => null;
    }

    private class StubLayoutEngine : ILayoutEngine
    {
        public string LayoutId => "stub";
        public string DisplayName => "Stub";
        public string? Icon => null;
        public LayoutCapabilities Capabilities => LayoutCapabilities.None;
        public bool RequiresCustomRenderer => false;
        public string ExtensionId => "test.stub.layout";
        public string Name => "Stub Layout";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
        public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context) => throw new NotImplementedException();
        public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context) => throw new NotImplementedException();
        public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
        public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
        public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;
        public Dictionary<string, object> GetLayoutMetadata() => new();
        public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context) => Task.CompletedTask;
    }

    private class StubExtensionHostContext : IExtensionHostContext
    {
        public IReadOnlyList<IExtension> GetLoadedExtensions() => Array.Empty<IExtension>();
        public IReadOnlyList<ILanguageKernel> GetKernels() => Array.Empty<ILanguageKernel>();
        public IReadOnlyList<ICellRenderer> GetRenderers() => Array.Empty<ICellRenderer>();
        public IReadOnlyList<IDataFormatter> GetFormatters() => Array.Empty<IDataFormatter>();
        public IReadOnlyList<ICellType> GetCellTypes() => Array.Empty<ICellType>();
        public IReadOnlyList<INotebookSerializer> GetSerializers() => Array.Empty<INotebookSerializer>();
        public IReadOnlyList<ILayoutEngine> GetLayouts() => Array.Empty<ILayoutEngine>();
        public IReadOnlyList<ITheme> GetThemes() => Array.Empty<ITheme>();
        public IReadOnlyList<INotebookPostProcessor> GetPostProcessors() => Array.Empty<INotebookPostProcessor>();
        public IReadOnlyList<ExtensionInfo> GetExtensionInfos() => Array.Empty<ExtensionInfo>();
        public Task EnableExtensionAsync(string extensionId) => Task.CompletedTask;
        public Task DisableExtensionAsync(string extensionId) => Task.CompletedTask;
    }

    #endregion
}
