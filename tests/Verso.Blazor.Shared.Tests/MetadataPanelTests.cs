namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class MetadataPanelTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService
        {
            IsLoaded = true,
            Title = "My Notebook",
            DefaultKernelId = "csharp",
            FilePath = "/home/user/notebook.verso",
            Created = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2024, 6, 20, 14, 0, 0, TimeSpan.Zero),
            FormatVersion = "1.0",
            RegisteredLanguages = new List<string> { "csharp", "fsharp", "sql" }
        };
    }

    [TestMethod]
    public void NotLoaded_ShowsNotLoadedState()
    {
        _service.IsLoaded = false;

        var cut = RenderComponent<MetadataPanel>(p => p
            .Add(m => m.Service, _service));

        // Should not show metadata fields when not loaded
        Assert.IsFalse(cut.Markup.Contains("My Notebook"));
    }

    [TestMethod]
    public void Loaded_RendersTitle()
    {
        var cut = RenderComponent<MetadataPanel>(p => p
            .Add(m => m.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("My Notebook"));
    }

    [TestMethod]
    public void Loaded_RendersDefaultKernel()
    {
        var cut = RenderComponent<MetadataPanel>(p => p
            .Add(m => m.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("csharp"));
    }

    [TestMethod]
    public void Loaded_RendersFilePath()
    {
        var cut = RenderComponent<MetadataPanel>(p => p
            .Add(m => m.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("/home/user/notebook.verso")
            || cut.Markup.Contains("notebook.verso"));
    }

    [TestMethod]
    public void Loaded_RendersFormatVersion()
    {
        var cut = RenderComponent<MetadataPanel>(p => p
            .Add(m => m.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("1.0"));
    }

    [TestMethod]
    public void Loaded_RendersCreatedDate()
    {
        var cut = RenderComponent<MetadataPanel>(p => p
            .Add(m => m.Service, _service));

        // Date should appear in some format
        Assert.IsTrue(cut.Markup.Contains("2024") || cut.Markup.Contains("Jan"));
    }

    [TestMethod]
    public void Loaded_RendersModifiedDate()
    {
        var cut = RenderComponent<MetadataPanel>(p => p
            .Add(m => m.Service, _service));

        Assert.IsTrue(cut.Markup.Contains("2024"));
    }

    [TestMethod]
    public void NullTitle_HandledGracefully()
    {
        _service.Title = null;

        var cut = RenderComponent<MetadataPanel>(p => p
            .Add(m => m.Service, _service));

        // Should render without errors
        Assert.IsNotNull(cut.Markup);
    }

    [TestMethod]
    public void NullFilePath_HandledGracefully()
    {
        _service.FilePath = null;

        var cut = RenderComponent<MetadataPanel>(p => p
            .Add(m => m.Service, _service));

        Assert.IsNotNull(cut.Markup);
    }
}
