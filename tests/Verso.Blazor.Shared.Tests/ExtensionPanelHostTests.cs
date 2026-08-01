using Bunit.JSInterop;
using Verso.Abstractions;
using Verso.Blazor.Shared.Models;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class ExtensionPanelHostTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService { IsLoaded = true };
        TestContext!.Services.AddSingleton<INotebookService>(_service);

        TestContext!.JSInterop.SetupVoid("versoPanelInteract.attach", _ => true);
        TestContext!.JSInterop.SetupVoid("versoPanelInteract.detach", _ => true);
    }

    private IRenderedComponent<ExtensionPanelHost> Render() =>
        RenderComponent<ExtensionPanelHost>(p => p
            .Add(c => c.Service, _service)
            .Add(c => c.ExtensionId, "com.test.panels")
            .Add(c => c.PanelId, "findings"));

    [TestMethod]
    public void HtmlRepresentation_IsRendered()
    {
        _service.PanelRepresentations = new()
        {
            new RenderResult("text/html", "<div class=\"marker\">from the extension</div>")
        };

        var cut = Render();

        Assert.IsNotNull(cut.Find(".marker"));
    }

    [TestMethod]
    public void RicherRepresentation_WinsOverSimplerOne()
    {
        // Representations arrive richest first and the host takes the first media type
        // it can draw, so the plain-text alternative must not be what shows up here.
        _service.PanelRepresentations = new()
        {
            new RenderResult("text/html", "<div class=\"marker\">markup</div>"),
            new RenderResult("text/plain", "plain text fallback")
        };

        var cut = Render();

        Assert.IsNotNull(cut.Find(".marker"));
        Assert.AreEqual(0, cut.FindAll(".verso-panel-plain").Count);
    }

    [TestMethod]
    public void PlainTextOnly_IsRenderedAsText()
    {
        _service.PanelRepresentations = new() {new RenderResult("text/plain", "3 open findings")};

        var cut = Render();

        StringAssert.Contains(cut.Find(".verso-panel-plain").TextContent, "3 open findings");
    }

    [TestMethod]
    public void UnsupportedMediaTypeOnly_ShowsTheEmptyState()
    {
        // A panel offering nothing this host can draw is not an error. It shows as
        // having nothing to say, which is what a non-web host would do too.
        _service.PanelRepresentations = new()
        {
            new RenderResult("application/vnd.example.custom", "opaque payload")
        };

        var cut = Render();

        Assert.IsNotNull(cut.Find(".verso-panel-empty"));
    }

    [TestMethod]
    public void NoRepresentations_ShowsTheEmptyState()
    {
        _service.PanelRepresentations = new() {};

        var cut = Render();

        Assert.IsNotNull(cut.Find(".verso-panel-empty"));
    }

    [TestMethod]
    public async Task PanelAction_IsRoutedToTheOwningPanel()
    {
        _service.PanelRepresentations = new() {new RenderResult("text/html", "<div>x</div>")};
        var cut = Render();

        await cut.InvokeAsync(() =>
            cut.Instance.OnPanelAction("accept", "f-17", "{\"note\":\"ok\"}"));

        Assert.AreEqual(1, _service.PanelInteractions.Count);
        var call = _service.PanelInteractions[0];
        Assert.AreEqual("com.test.panels", call.ExtensionId);
        Assert.AreEqual("findings", call.PanelId);
        Assert.AreEqual("accept", call.InteractionType);
        Assert.AreEqual("f-17", call.TargetId);
        Assert.AreEqual("{\"note\":\"ok\"}", call.Payload);
    }

    [TestMethod]
    public void PanelUpdated_ForThisPanel_RefetchesContent()
    {
        _service.PanelRepresentations = new() {new RenderResult("text/html", "<div class=\"first\"></div>")};
        var cut = Render();

        _service.PanelRepresentations = new() {new RenderResult("text/html", "<div class=\"second\"></div>")};
        cut.InvokeAsync(() =>
            _service.RaisePanelUpdated(new PanelUpdatedEventArgs("com.test.panels", "findings")))
            .GetAwaiter().GetResult();

        cut.WaitForAssertion(() => Assert.IsNotNull(cut.Find(".second")));
    }

    [TestMethod]
    public void PanelUpdated_ForAnotherPanel_IsIgnored()
    {
        _service.PanelRepresentations = new() {new RenderResult("text/html", "<div class=\"first\"></div>")};
        var cut = Render();

        _service.PanelRepresentations = new() {new RenderResult("text/html", "<div class=\"second\"></div>")};
        cut.InvokeAsync(() =>
            _service.RaisePanelUpdated(new PanelUpdatedEventArgs("com.other.ext", "findings")))
            .GetAwaiter().GetResult();

        Assert.IsNotNull(cut.Find(".first"));
    }
}
