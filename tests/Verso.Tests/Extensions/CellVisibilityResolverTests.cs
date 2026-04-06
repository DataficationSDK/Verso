using System.Text.Json;
using Verso.Extensions.Utilities;

namespace Verso.Tests.Extensions;

[TestClass]
public class CellVisibilityResolverTests
{
    private static readonly IReadOnlySet<CellVisibilityState> NotebookStates =
        new HashSet<CellVisibilityState> { CellVisibilityState.Visible };

    private static readonly IReadOnlySet<CellVisibilityState> PresentationStates =
        new HashSet<CellVisibilityState>
        {
            CellVisibilityState.Visible,
            CellVisibilityState.Hidden,
            CellVisibilityState.OutputOnly,
        };

    private static readonly IReadOnlySet<CellVisibilityState> DashboardStates =
        new HashSet<CellVisibilityState>
        {
            CellVisibilityState.Visible,
            CellVisibilityState.Hidden,
        };

    [TestMethod]
    public void Resolve_UserOverridePresent_ReturnsOverride()
    {
        var cell = CellWithVisibilityOverride("presentation", "hidden");
        var renderer = new StubRenderer(CellVisibilityHint.Content);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "presentation", PresentationStates);

        Assert.AreEqual(CellVisibilityState.Hidden, result);
    }

    [TestMethod]
    public void Resolve_NoOverride_ContentHint_ReturnsVisible()
    {
        var cell = new CellModel();
        var renderer = new StubRenderer(CellVisibilityHint.Content);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "presentation", PresentationStates);

        Assert.AreEqual(CellVisibilityState.Visible, result);
    }

    [TestMethod]
    public void Resolve_NoOverride_InfrastructureHint_RestrictiveLayout_ReturnsHidden()
    {
        var cell = new CellModel();
        var renderer = new StubRenderer(CellVisibilityHint.Infrastructure);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "presentation", PresentationStates);

        Assert.AreEqual(CellVisibilityState.Hidden, result);
    }

    [TestMethod]
    public void Resolve_NoOverride_InfrastructureHint_NotebookLayout_ReturnsVisible()
    {
        var cell = new CellModel();
        var renderer = new StubRenderer(CellVisibilityHint.Infrastructure);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "notebook", NotebookStates);

        Assert.AreEqual(CellVisibilityState.Visible, result);
    }

    [TestMethod]
    public void Resolve_NoOverride_OutputOnlyHint_LayoutSupportsOutputOnly_ReturnsOutputOnly()
    {
        var cell = new CellModel();
        var renderer = new StubRenderer(CellVisibilityHint.OutputOnly);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "presentation", PresentationStates);

        Assert.AreEqual(CellVisibilityState.OutputOnly, result);
    }

    [TestMethod]
    public void Resolve_NoOverride_OutputOnlyHint_LayoutDoesNotSupportOutputOnly_ReturnsVisible()
    {
        var cell = new CellModel();
        var renderer = new StubRenderer(CellVisibilityHint.OutputOnly);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "dashboard", DashboardStates);

        Assert.AreEqual(CellVisibilityState.Visible, result);
    }

    [TestMethod]
    public void Resolve_OverrideUnsupportedState_FallsBackToNearestSupported()
    {
        // Dashboard doesn't support OutputOnly; should fall back to Visible
        var cell = CellWithVisibilityOverride("dashboard", "outputonly");
        var renderer = new StubRenderer(CellVisibilityHint.Content);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "dashboard", DashboardStates);

        Assert.AreEqual(CellVisibilityState.Visible, result);
    }

    [TestMethod]
    public void Resolve_CollapsedOverride_FallsBackToHiddenThenVisible()
    {
        // Dashboard supports Hidden, so Collapsed should fall to Hidden
        var cell = CellWithVisibilityOverride("dashboard", "collapsed");
        var renderer = new StubRenderer(CellVisibilityHint.Content);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "dashboard", DashboardStates);

        Assert.AreEqual(CellVisibilityState.Hidden, result);

        // Notebook only supports Visible, so Collapsed should fall to Visible
        var result2 = CellVisibilityResolver.Resolve(cell, renderer, "dashboard", NotebookStates);

        Assert.AreEqual(CellVisibilityState.Visible, result2);
    }

    [TestMethod]
    public void Resolve_EmptyMetadata_FallsThroughToDefaults()
    {
        var cell = new CellModel();
        var renderer = new StubRenderer(CellVisibilityHint.OutputOnly);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "presentation", PresentationStates);

        Assert.AreEqual(CellVisibilityState.OutputOnly, result);
    }

    [TestMethod]
    public void Resolve_MetadataKeyMissing_FallsThroughToDefaults()
    {
        var cell = new CellModel();
        cell.Metadata["some-other-key"] = "value";
        var renderer = new StubRenderer(CellVisibilityHint.Infrastructure);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "presentation", PresentationStates);

        Assert.AreEqual(CellVisibilityState.Hidden, result);
    }

    [TestMethod]
    public void Resolve_JsonElementMetadata_ReadsOverride()
    {
        var json = "{\"presentation\":\"outputonly\",\"dashboard\":\"hidden\"}";
        var jsonElement = JsonDocument.Parse(json).RootElement.Clone();

        var cell = new CellModel();
        cell.Metadata["verso:visibility"] = jsonElement;
        var renderer = new StubRenderer(CellVisibilityHint.Content);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "presentation", PresentationStates);

        Assert.AreEqual(CellVisibilityState.OutputOnly, result);
    }

    [TestMethod]
    public void Resolve_DictionaryStringObjectMetadata_ReadsOverride()
    {
        var cell = new CellModel();
        cell.Metadata["verso:visibility"] = new Dictionary<string, object>
        {
            ["presentation"] = "visible",
        };
        var renderer = new StubRenderer(CellVisibilityHint.Infrastructure);

        var result = CellVisibilityResolver.Resolve(cell, renderer, "presentation", PresentationStates);

        Assert.AreEqual(CellVisibilityState.Visible, result);
    }

    #region Helpers

    private static CellModel CellWithVisibilityOverride(string layoutId, string state)
    {
        var cell = new CellModel();
        cell.Metadata["verso:visibility"] = new Dictionary<string, string>
        {
            [layoutId] = state,
        };
        return cell;
    }

    private class StubRenderer : ICellRenderer
    {
        private readonly CellVisibilityHint _hint;

        public StubRenderer(CellVisibilityHint hint) => _hint = hint;

        CellVisibilityHint ICellRenderer.DefaultVisibility => _hint;

        public string CellTypeId => "stub";
        public string DisplayName => "Stub";
        public string ExtensionId => "test.stub";
        public string Name => "Stub";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
        public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context) => throw new NotImplementedException();
        public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context) => throw new NotImplementedException();
        public string? GetEditorLanguage() => null;
    }

    #endregion
}
