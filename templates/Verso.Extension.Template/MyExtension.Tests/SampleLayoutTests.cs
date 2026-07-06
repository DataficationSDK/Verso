namespace MyExtension.Tests;

[TestClass]
public sealed class SampleLayoutTests
{
    private readonly SampleLayout _layout = new();

    private static List<CellModel> MakeCells(int count)
    {
        var cells = new List<CellModel>();
        for (var i = 0; i < count; i++)
            cells.Add(new CellModel { Type = "code", Language = "csharp", Source = $"// cell {i}" });
        return cells;
    }

    [TestMethod]
    public void ExtensionId_IsSet()
    {
        Assert.IsFalse(string.IsNullOrEmpty(_layout.ExtensionId));
    }

    [TestMethod]
    public async Task Render_EmitsSlotPerCell()
    {
        var cells = MakeCells(3);
        var result = await _layout.RenderLayoutAsync(cells, new StubVersoContext());

        Assert.AreEqual("text/html", result.MimeType);
        foreach (var cell in cells)
            Assert.IsTrue(result.Content.Contains($"data-cell-slot=\"{cell.Id}\""));
    }

    [TestMethod]
    public async Task Interaction_SetDensity_RequestsRenderAndPersists()
    {
        var renderRequested = false;
        var context = new LayoutInteractionContext
        {
            ExtensionId = _layout.ExtensionId,
            LayoutId = _layout.LayoutId,
            InteractionType = "set-density",
            Payload = "compact",
            Verso = new StubVersoContext(),
            RequestRender = () => renderRequested = true,
        };

        await _layout.OnLayoutInteractionAsync(context);

        Assert.IsTrue(renderRequested);
        Assert.AreEqual("compact", _layout.GetLayoutMetadata()["density"]);
    }

    [TestMethod]
    public async Task Interaction_UnknownDensity_IsIgnored()
    {
        var renderRequested = false;
        var context = new LayoutInteractionContext
        {
            InteractionType = "set-density",
            Payload = "sideways",
            Verso = new StubVersoContext(),
            RequestRender = () => renderRequested = true,
        };

        await _layout.OnLayoutInteractionAsync(context);

        Assert.IsFalse(renderRequested);
        Assert.AreEqual("comfortable", _layout.GetLayoutMetadata()["density"]);
    }

    [TestMethod]
    public async Task Metadata_RoundTrips()
    {
        await _layout.ApplyLayoutMetadata(
            new Dictionary<string, object> { ["density"] = "compact" },
            new StubVersoContext());

        var result = await _layout.RenderLayoutAsync(MakeCells(1), new StubVersoContext());

        Assert.AreEqual("compact", _layout.GetLayoutMetadata()["density"]);
        Assert.IsTrue(result.Content.Contains("compact"));
    }

    [TestMethod]
    public async Task CellContainer_ReflectsRenderOrder()
    {
        var cells = MakeCells(2);
        await _layout.RenderLayoutAsync(cells, new StubVersoContext());

        var first = await _layout.GetCellContainerAsync(cells[0].Id, new StubVersoContext());
        var second = await _layout.GetCellContainerAsync(cells[1].Id, new StubVersoContext());

        Assert.AreEqual(0, first.Y);
        Assert.IsTrue(second.Y > first.Y);
    }

    [TestMethod]
    public async Task StaticAssets_ReturnsThemedCssWhenSupported()
    {
        var capabilities = new LayoutHostCapabilities(
            new HashSet<string> { "text/css" },
            new HashSet<string> { "text/html" });

        var assets = await _layout.GetStaticAssetsAsync(new StubVersoContext(), capabilities);

        Assert.IsNotNull(assets);
        Assert.AreEqual(1, assets.Count);
        Assert.AreEqual("text/css", assets[0].ContentType);
    }
}
