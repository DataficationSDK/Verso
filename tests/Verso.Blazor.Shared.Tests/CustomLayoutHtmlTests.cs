using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class CustomLayoutHtmlTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService
        {
            IsLoaded = true,
            ActiveLayout = new LayoutReference("com.test.viz", "grid"),
            ActiveLayoutRendererIsolation = "inline",
            RenderActiveLayoutResult =
                "<div class=\"viz\"><div data-cell-slot=\"00000000-0000-0000-0000-000000000001\"></div></div>"
        };
        TestContext!.Services.AddSingleton<INotebookService>(_service);

        TestContext!.JSInterop.SetupVoid("versoCustomLayout.mountSlots", _ => true);
        TestContext!.JSInterop.SetupVoid("versoCustomLayout.unmountSlots", _ => true);
    }

    [TestMethod]
    public void OnFirstRender_CallsRenderActiveLayout()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));

        // OnAfterRenderAsync(firstRender=true) drives the initial fetch.
        Assert.AreEqual(1, _service.RenderActiveLayoutCallCount,
            "Expected RenderActiveLayoutAsync to be invoked exactly once on first render.");
    }

    [TestMethod]
    public void Rendered_WrapsHtmlInLayoutRootWithIdentityAttributes()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));

        cut.WaitForAssertion(() =>
        {
            Assert.IsTrue(cut.Markup.Contains("verso-layout-root"));
            Assert.IsTrue(cut.Markup.Contains("data-extension-id=\"com.test.viz\""));
            Assert.IsTrue(cut.Markup.Contains("data-layout-id=\"grid\""));
            Assert.IsTrue(cut.Markup.Contains("data-cell-slot=\"00000000-0000-0000-0000-000000000001\""));
        });
    }

    [TestMethod]
    public void OnLayoutChanged_RefetchesAndUpdatesMarkup()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        _service.RenderActiveLayoutResult = "<div class=\"viz-v2\"><span>updated</span></div>";
        cut.InvokeAsync(() => _service.RaiseLayoutChanged()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual(2, _service.RenderActiveLayoutCallCount,
                "Expected a re-fetch after OnLayoutChanged.");
            Assert.IsTrue(cut.Markup.Contains("viz-v2"));
        });
    }

    [TestMethod]
    public void OnFirstRender_InvokesMountSlots()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p
            .Add(c => c.Service, _service)
            .Add(c => c.CellPoolRef, new ElementReference()));

        cut.WaitForAssertion(() =>
        {
            var calls = TestContext!.JSInterop.Invocations
                .Where(i => i.Identifier == "versoCustomLayout.mountSlots")
                .ToList();
            Assert.IsTrue(calls.Count >= 1,
                "Expected at least one versoCustomLayout.mountSlots interop call after first render.");
        });
    }

    [TestMethod]
    public void OnLayoutChanged_InvokesUnmountSlotsBeforeSwap()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p
            .Add(c => c.Service, _service)
            .Add(c => c.CellPoolRef, new ElementReference()));

        cut.WaitForAssertion(() =>
        {
            Assert.IsTrue(TestContext!.JSInterop.Invocations.Any(i => i.Identifier == "versoCustomLayout.mountSlots"));
        });

        var unmountBefore = TestContext!.JSInterop.Invocations.Count(i => i.Identifier == "versoCustomLayout.unmountSlots");

        _service.RenderActiveLayoutResult = "<div class=\"viz-v2\"></div>";
        cut.InvokeAsync(() => _service.RaiseLayoutChanged()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            var unmountAfter = TestContext!.JSInterop.Invocations.Count(i => i.Identifier == "versoCustomLayout.unmountSlots");
            Assert.IsTrue(unmountAfter > unmountBefore,
                "Expected versoCustomLayout.unmountSlots to be invoked when the layout changes.");
        });
    }

    [TestMethod]
    public void Dispose_UnsubscribesFromOnLayoutChanged()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        var before = _service.RenderActiveLayoutCallCount;
        TestContext!.DisposeComponents();

        // After dispose, raising the event must not trigger another render fetch.
        _service.RaiseLayoutChanged();

        Assert.AreEqual(before, _service.RenderActiveLayoutCallCount,
            "Expected the component to have unsubscribed from OnLayoutChanged on dispose.");
    }
}
