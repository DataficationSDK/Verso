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
        TestContext!.JSInterop.SetupVoid("versoCustomLayout.updateSlotPosition", _ => true);
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
    public void OnFirstRender_WritesFrameInstanceIdAttribute()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));

        cut.WaitForAssertion(() =>
        {
            var value = cut.Find(".verso-layout-root").GetAttribute("data-frame-instance-id");
            Assert.IsFalse(string.IsNullOrEmpty(value),
                "Expected data-frame-instance-id to be non-empty after first render.");
            Assert.IsTrue(value!.Contains("/grid/"),
                $"Expected data-frame-instance-id to contain '/grid/'; got '{value}'.");
        });
    }

    [TestMethod]
    public void FrameInstanceId_StableAcrossNonLayoutRenders()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        var before = cut.Find(".verso-layout-root").GetAttribute("data-frame-instance-id");

        // Force a re-render that does NOT raise OnLayoutChanged.
        cut.Render();

        var after = cut.Find(".verso-layout-root").GetAttribute("data-frame-instance-id");
        Assert.AreEqual(before, after,
            "Expected data-frame-instance-id to remain stable across non-layout re-renders.");
    }

    [TestMethod]
    public void FrameInstanceId_NewIdOnLayoutChange()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        var before = cut.Find(".verso-layout-root").GetAttribute("data-frame-instance-id");

        _service.RenderActiveLayoutResult = "<div class=\"viz-v2\"></div>";
        cut.InvokeAsync(() => _service.RaiseLayoutChanged()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            var after = cut.Find(".verso-layout-root").GetAttribute("data-frame-instance-id");
            Assert.AreNotEqual(before, after,
                "Expected data-frame-instance-id to be reallocated after a layout change.");
        });
    }

    // ─── layout/updated subscription ──────────────────────────────────────────

    private LayoutUpdatedEventArgs MatchingFullScope(string frameInstanceId = "") =>
        new("com.test.viz", "grid", frameInstanceId, "full", null);

    [TestMethod]
    public void OnLayoutUpdated_FullScope_RefetchesAndUpdatesMarkup()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        _service.RenderActiveLayoutResult = "<div class=\"viz-updated\">refreshed</div>";
        cut.InvokeAsync(() => _service.RaiseLayoutUpdated(MatchingFullScope())).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual(2, _service.RenderActiveLayoutCallCount,
                "scope=full must re-fetch layout/render.");
            Assert.IsTrue(cut.Markup.Contains("viz-updated"));
        });
    }

    [TestMethod]
    public void OnLayoutUpdated_DifferentLayoutId_Ignored()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        cut.InvokeAsync(() => _service.RaiseLayoutUpdated(
            new LayoutUpdatedEventArgs("com.test.viz", "other-layout", "", "full", null)))
            .GetAwaiter().GetResult();

        Assert.AreEqual(1, _service.RenderActiveLayoutCallCount,
            "A notification for a different layoutId must not trigger a re-fetch.");
    }

    [TestMethod]
    public void OnLayoutUpdated_DifferentExtensionId_Ignored()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        cut.InvokeAsync(() => _service.RaiseLayoutUpdated(
            new LayoutUpdatedEventArgs("com.other.ext", "grid", "", "full", null)))
            .GetAwaiter().GetResult();

        Assert.AreEqual(1, _service.RenderActiveLayoutCallCount,
            "A notification for a different extensionId must not trigger a re-fetch.");
    }

    [TestMethod]
    public void OnLayoutUpdated_DifferentFrameInstanceId_Ignored()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        cut.InvokeAsync(() => _service.RaiseLayoutUpdated(
            new LayoutUpdatedEventArgs("com.test.viz", "grid", "nb-1/grid/999", "full", null)))
            .GetAwaiter().GetResult();

        Assert.AreEqual(1, _service.RenderActiveLayoutCallCount,
            "A notification targeted at a different renderer instance must not trigger a re-fetch.");
    }

    [TestMethod]
    public void OnLayoutUpdated_EmptyFrameInstanceId_TreatedAsBroadcast()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        // Empty frameInstanceId should match any instance (broadcast).
        cut.InvokeAsync(() => _service.RaiseLayoutUpdated(MatchingFullScope())).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
            Assert.AreEqual(2, _service.RenderActiveLayoutCallCount,
                "Empty frameInstanceId on the notification must broadcast to all instances."));
    }

    [TestMethod]
    public void OnLayoutUpdated_CellScope_InvokesUpdateSlotPosition()
    {
        var cellId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        _service.CellContainers[cellId] = new CellContainerInfo(cellId, X: 2, Y: 3, Width: 4, Height: 5);

        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        cut.InvokeAsync(() => _service.RaiseLayoutUpdated(
            new LayoutUpdatedEventArgs("com.test.viz", "grid", "", "cell", cellId)))
            .GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            var call = TestContext!.JSInterop.Invocations
                .FirstOrDefault(i => i.Identifier == "versoCustomLayout.updateSlotPosition");
            Assert.IsNotNull(call, "Expected versoCustomLayout.updateSlotPosition to be invoked.");

            // Args: rootRef, cellId, row (=Y), col (=X), width, height
            Assert.AreEqual(cellId.ToString(), call.Arguments[1]);
            Assert.AreEqual(3.0, Convert.ToDouble(call.Arguments[2]));
            Assert.AreEqual(2.0, Convert.ToDouble(call.Arguments[3]));
            Assert.AreEqual(4.0, Convert.ToDouble(call.Arguments[4]));
            Assert.AreEqual(5.0, Convert.ToDouble(call.Arguments[5]));
        });

        Assert.AreEqual(1, _service.RenderActiveLayoutCallCount,
            "scope=cell must NOT trigger a full layout/render.");
    }

    [TestMethod]
    public void OnLayoutUpdated_MetadataScope_NoOp()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        cut.InvokeAsync(() => _service.RaiseLayoutUpdated(
            new LayoutUpdatedEventArgs("com.test.viz", "grid", "", "metadata", null)))
            .GetAwaiter().GetResult();

        Assert.AreEqual(1, _service.RenderActiveLayoutCallCount,
            "scope=metadata must not trigger a re-fetch in v1.0.");
        Assert.IsFalse(TestContext!.JSInterop.Invocations.Any(i =>
            i.Identifier == "versoCustomLayout.updateSlotPosition"),
            "scope=metadata must not invoke updateSlotPosition.");
    }

    [TestMethod]
    public void Dispose_UnsubscribesFromOnLayoutUpdated()
    {
        var cut = RenderComponent<CustomLayoutHtml>(p => p.Add(c => c.Service, _service));
        cut.WaitForAssertion(() => Assert.AreEqual(1, _service.RenderActiveLayoutCallCount));

        var before = _service.RenderActiveLayoutCallCount;
        TestContext!.DisposeComponents();

        _service.RaiseLayoutUpdated(MatchingFullScope());

        Assert.AreEqual(before, _service.RenderActiveLayoutCallCount,
            "Expected the component to have unsubscribed from OnLayoutUpdated on dispose.");
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
