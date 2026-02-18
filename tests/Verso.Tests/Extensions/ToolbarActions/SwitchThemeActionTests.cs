using Verso.Abstractions;
using Verso.Extensions.ToolbarActions;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions.ToolbarActions;

[TestClass]
public sealed class SwitchThemeActionTests
{
    [TestMethod]
    public void Metadata_IsCorrect()
    {
        var action = new SwitchThemeAction();
        Assert.AreEqual("verso.switchTheme", action.ActionId);
        Assert.AreEqual(ToolbarPlacement.MainToolbar, action.Placement);
        Assert.AreEqual(55, action.Order);
    }

    [TestMethod]
    public async Task IsEnabled_True_WhenMultipleThemes()
    {
        var action = new SwitchThemeAction();
        var context = CreateContext(themeCount: 2);

        Assert.IsTrue(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task IsEnabled_False_WhenSingleTheme()
    {
        var action = new SwitchThemeAction();
        var context = CreateContext(themeCount: 1);

        Assert.IsFalse(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task IsEnabled_False_WhenNoThemes()
    {
        var action = new SwitchThemeAction();
        var context = CreateContext(themeCount: 0);

        Assert.IsFalse(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task Execute_CyclesToNextTheme()
    {
        var action = new SwitchThemeAction();
        var notebook = new StubNotebookOperations { ActiveThemeId = "theme-a" };
        var context = CreateContext(
            themeIds: new[] { "theme-a", "theme-b", "theme-c" },
            notebook: notebook);

        await action.ExecuteAsync(context);

        Assert.AreEqual("theme-b", notebook.ActiveThemeId);
    }

    [TestMethod]
    public async Task Execute_WrapsAroundToFirst()
    {
        var action = new SwitchThemeAction();
        var notebook = new StubNotebookOperations { ActiveThemeId = "theme-c" };
        var context = CreateContext(
            themeIds: new[] { "theme-a", "theme-b", "theme-c" },
            notebook: notebook);

        await action.ExecuteAsync(context);

        Assert.AreEqual("theme-a", notebook.ActiveThemeId);
    }

    [TestMethod]
    public async Task Execute_NoActiveTheme_SelectsFirst()
    {
        var action = new SwitchThemeAction();
        var notebook = new StubNotebookOperations { ActiveThemeId = null };
        var context = CreateContext(
            themeIds: new[] { "theme-a", "theme-b" },
            notebook: notebook);

        await action.ExecuteAsync(context);

        // currentIndex=-1, next=(−1+1)%2=0 → "theme-a"
        Assert.AreEqual("theme-a", notebook.ActiveThemeId);
    }

    private static StubToolbarActionContext CreateContext(
        int themeCount = 2,
        string[]? themeIds = null,
        StubNotebookOperations? notebook = null)
    {
        themeIds ??= Enumerable.Range(0, themeCount).Select(i => $"theme-{i}").ToArray();
        var themes = themeIds.Select(id => new StubTheme(id)).ToArray();

        return new StubToolbarActionContext
        {
            ExtensionHost = new ThemeAwareExtensionHostContext(themes),
            Notebook = notebook ?? new StubNotebookOperations()
        };
    }

    private sealed class StubTheme : ITheme
    {
        public StubTheme(string themeId) => ThemeId = themeId;

        public string ExtensionId => $"test.theme.{ThemeId}";
        public string Name => ThemeId;
        public string Version => "0.1.0";
        public string? Author => null;
        public string? Description => null;
        public string ThemeId { get; }
        public string DisplayName => ThemeId;
        public ThemeKind ThemeKind => ThemeKind.Light;
        public ThemeColorTokens Colors => new();
        public ThemeTypography Typography => new();
        public ThemeSpacing Spacing => new();
        public SyntaxColorMap GetSyntaxColors() => new();
        public string? GetCustomToken(string key) => null;
        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
    }

    private sealed class ThemeAwareExtensionHostContext : IExtensionHostContext
    {
        private readonly IReadOnlyList<ITheme> _themes;

        public ThemeAwareExtensionHostContext(IReadOnlyList<ITheme> themes)
            => _themes = themes;

        public IReadOnlyList<IExtension> GetLoadedExtensions() => Array.Empty<IExtension>();
        public IReadOnlyList<ILanguageKernel> GetKernels() => Array.Empty<ILanguageKernel>();
        public IReadOnlyList<ICellRenderer> GetRenderers() => Array.Empty<ICellRenderer>();
        public IReadOnlyList<IDataFormatter> GetFormatters() => Array.Empty<IDataFormatter>();
        public IReadOnlyList<ICellType> GetCellTypes() => Array.Empty<ICellType>();
        public IReadOnlyList<INotebookSerializer> GetSerializers() => Array.Empty<INotebookSerializer>();
        public IReadOnlyList<ILayoutEngine> GetLayouts() => Array.Empty<ILayoutEngine>();
        public IReadOnlyList<ITheme> GetThemes() => _themes;
        public IReadOnlyList<INotebookPostProcessor> GetPostProcessors() => Array.Empty<INotebookPostProcessor>();
        public IReadOnlyList<ExtensionInfo> GetExtensionInfos() => Array.Empty<ExtensionInfo>();
        public Task EnableExtensionAsync(string extensionId) => Task.CompletedTask;
        public Task DisableExtensionAsync(string extensionId) => Task.CompletedTask;
    }
}
