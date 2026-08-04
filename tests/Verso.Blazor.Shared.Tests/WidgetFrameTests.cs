namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// Covers what a widget frame tells the rest of the page about itself: whether the output it draws
/// is still in conversation with something, and which channel the interop should bind it to.
/// </summary>
[TestClass]
public sealed class WidgetFrameTests : BunitTestContext
{
    [TestMethod]
    public void AWidgetWithAChannelIsMountedAgainstIt()
    {
        var channelId = "8bf0b1c24f7e4a5f9d2c6e1a0b3d5f70";
        var cut = RenderWidget(CellOutput.Widget("<html><body>live</body></html>", channelId));

        var mount = TestContext!.JSInterop.Invocations["versoWidget.mount"].Single();

        // The frame element, the document, the theme hint the interop reads for itself, and the
        // channel. The id is handed over here rather than written into the document, so it is never
        // part of what a save would write.
        Assert.AreEqual(4, mount.Arguments.Count);
        Assert.AreEqual("<html><body>live</body></html>", mount.Arguments[1]);
        Assert.AreEqual(channelId, mount.Arguments[3]);
    }

    [TestMethod]
    public void AWidgetWithAChannelSaysItIsLive()
    {
        var cut = RenderWidget(CellOutput.Widget("<html><body>live</body></html>", "abc123"));

        Assert.AreEqual("true", cut.Find("iframe").GetAttribute("data-verso-live"));
    }

    [TestMethod]
    public void AWidgetWithoutAChannelSaysItIsNot()
    {
        // What a widget restored from a saved file looks like, and what every widget looks like on
        // a host with nothing to talk back to. It draws the same way and simply has no return leg.
        var cut = RenderWidget(CellOutput.Widget("<html><body>saved</body></html>"));

        var frame = cut.Find("iframe");
        Assert.AreEqual("false", frame.GetAttribute("data-verso-live"));

        var mount = TestContext!.JSInterop.Invocations["versoWidget.mount"].Single();
        Assert.IsNull(mount.Arguments[3]);
    }

    private IRenderedComponent<OutputView> RenderWidget(CellOutput output)
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        return TestContext.RenderComponent<OutputView>(parameters => parameters
            .Add(p => p.Outputs, new[] { output })
            .Add(p => p.ExtendedMimeTypes, true));
    }
}
