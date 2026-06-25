using System.Text;
using Verso.Abstractions;
using Verso.Sample.Sparkline;
using Verso.Testing.Stubs;

namespace Verso.Sample.Sparkline.Tests;

[TestClass]
public sealed class SparklineLayoutTests
{
    private readonly SparklineLayout _layout = new();
    private readonly StubVersoContext _context = new();

    private LayoutRendererMountContext MountContext(FakeFrameChannel frame) => new()
    {
        ExtensionId = _layout.ExtensionId,
        LayoutId = _layout.LayoutId,
        FrameInstanceId = frame.FrameInstanceId,
        Isolation = LayoutRendererIsolation.Isolated,
        Verso = _context,
        Frame = frame,
        CancellationToken = CancellationToken.None,
    };

    private LayoutRendererUnmountContext UnmountContext(string frameInstanceId) => new()
    {
        ExtensionId = _layout.ExtensionId,
        LayoutId = _layout.LayoutId,
        FrameInstanceId = frameInstanceId,
        Isolation = LayoutRendererIsolation.Isolated,
        Verso = _context,
        CancellationToken = CancellationToken.None,
    };

    private LayoutInteractionContext InteractionContext(string interactionType, string payload) => new()
    {
        ExtensionId = _layout.ExtensionId,
        LayoutId = _layout.LayoutId,
        FrameInstanceId = "nb/sparkline/0",
        InteractionType = interactionType,
        Payload = payload,
        Verso = _context,
        CancellationToken = CancellationToken.None,
    };

    private static double[]? ExtractValues(object? payload)
        => payload?.GetType().GetProperty("values")?.GetValue(payload) as double[];

    // --- Identity ---

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("com.verso.sample.sparkline", _layout.ExtensionId);

    [TestMethod]
    public void LayoutId_IsSparkline()
        => Assert.AreEqual("sparkline", _layout.LayoutId);

    [TestMethod]
    public void DisplayName_IsSparkline()
        => Assert.AreEqual("Sparkline", _layout.DisplayName);

    [TestMethod]
    public void RequiresCustomRenderer_IsTrue()
        => Assert.IsTrue(_layout.RequiresCustomRenderer);

    // --- Isolation / renderer package ---

    [TestMethod]
    public void RendererIsolation_IsIsolated()
        => Assert.AreEqual(LayoutRendererIsolation.Isolated, _layout.RendererIsolation);

    [TestMethod]
    public async Task GetRendererPackage_ReturnsSelfContainedMainJs()
    {
        var package = await _layout.GetRendererPackageAsync(_context);

        Assert.IsNotNull(package);
        Assert.AreEqual("main.js", package!.EntryPoint);
        Assert.IsTrue(package.Files.ContainsKey("main.js"));
        Assert.AreEqual(1, package.Files.Count, "The renderer must be a single self-contained file.");

        // Pure-canvas renderer needs no extra Content Security Policy.
        Assert.IsNull(package.ContentSecurityPolicy);
    }

    [TestMethod]
    public async Task GetRendererPackage_MainJs_UsesTheBridgeContract()
    {
        var package = await _layout.GetRendererPackageAsync(_context);
        var source = Encoding.UTF8.GetString(package!.Files["main.js"]);

        Assert.IsTrue(source.Contains("verso.ready"), "Entry module must signal readiness.");
        Assert.IsTrue(source.Contains("verso.onMessage"), "Entry module must subscribe to host messages.");
        Assert.IsTrue(source.Contains("select-point"), "Entry module must raise the select-point interaction.");
        Assert.IsTrue(source.Contains("createElement(\"canvas\")"), "Renderer draws on a canvas.");
        Assert.IsTrue(source.Contains("--verso-accent"), "Renderer reads host theme tokens.");
    }

    [TestMethod]
    public async Task RenderLayoutAsync_IsEmptyForIsolatedRenderer()
    {
        var result = await _layout.RenderLayoutAsync(new List<CellModel>(), _context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.AreEqual(string.Empty, result.Content);
    }

    // --- Lifecycle: mount seeds initial state ---

    [TestMethod]
    public async Task OnRendererMounted_SeedsInitialValuesFromVariable()
    {
        _context.Variables.Set(SparklineLayout.SeriesVariable, new double[] { 1, 4, 9 });
        var frame = new FakeFrameChannel("nb/sparkline/0");

        var init = await _layout.OnRendererMountedAsync(MountContext(frame));

        Assert.IsNotNull(init);
        Assert.AreEqual(SparklineLayout.SeriesVariable, init!["variable"]);
        CollectionAssert.AreEqual(new double[] { 1, 4, 9 }, (double[])init["values"]);
    }

    [TestMethod]
    public async Task OnRendererMounted_SeedsEmpty_WhenVariableMissing()
    {
        var frame = new FakeFrameChannel("nb/sparkline/0");

        var init = await _layout.OnRendererMountedAsync(MountContext(frame));

        Assert.IsNotNull(init);
        Assert.AreEqual(0, ((double[])init!["values"]).Length);
    }

    [TestMethod]
    public async Task OnRendererMounted_CoercesIntArray()
    {
        _context.Variables.Set(SparklineLayout.SeriesVariable, new[] { 3, 1, 2 });
        var frame = new FakeFrameChannel("nb/sparkline/0");

        var init = await _layout.OnRendererMountedAsync(MountContext(frame));

        CollectionAssert.AreEqual(new double[] { 3, 1, 2 }, (double[])init!["values"]);
    }

    // --- Lifecycle: live push on variable change ---

    [TestMethod]
    public async Task VariableChange_PushesDataToLiveFrame()
    {
        var frame = new FakeFrameChannel("nb/sparkline/0");
        await _layout.OnRendererMountedAsync(MountContext(frame));

        _context.Variables.Set(SparklineLayout.SeriesVariable, new double[] { 5, 6, 7 });

        var dataMessages = frame.Messages.Where(m => m.Type == "data").ToList();
        Assert.AreEqual(1, dataMessages.Count);
        CollectionAssert.AreEqual(new double[] { 5, 6, 7 }, ExtractValues(dataMessages[0].Payload));
    }

    [TestMethod]
    public async Task VariableChange_DoesNotPush_WhenFrameNotAlive()
    {
        var frame = new FakeFrameChannel("nb/sparkline/0") { IsAlive = false };
        await _layout.OnRendererMountedAsync(MountContext(frame));

        _context.Variables.Set(SparklineLayout.SeriesVariable, new double[] { 5, 6, 7 });

        Assert.AreEqual(0, frame.Messages.Count(m => m.Type == "data"));
    }

    [TestMethod]
    public async Task Unmount_StopsPushing()
    {
        var frame = new FakeFrameChannel("nb/sparkline/0");
        await _layout.OnRendererMountedAsync(MountContext(frame));

        await _layout.OnRendererUnmountedAsync(UnmountContext(frame.FrameInstanceId));
        var countAfterUnmount = frame.Messages.Count;

        _context.Variables.Set(SparklineLayout.SeriesVariable, new double[] { 8, 9 });

        Assert.AreEqual(countAfterUnmount, frame.Messages.Count, "No pushes should occur after unmount.");
    }

    // --- Interaction: select-point writes a kernel variable ---

    [TestMethod]
    public async Task SelectPoint_JsonPayload_SetsSelectedVariable()
    {
        await _layout.OnLayoutInteractionAsync(InteractionContext("select-point", "{\"index\":2,\"value\":9}"));

        Assert.AreEqual(2, _context.Variables.Get<int>(SparklineLayout.SelectedVariable));
    }

    [TestMethod]
    public async Task SelectPoint_BareIntPayload_SetsSelectedVariable()
    {
        await _layout.OnLayoutInteractionAsync(InteractionContext("select-point", "3"));

        Assert.AreEqual(3, _context.Variables.Get<int>(SparklineLayout.SelectedVariable));
    }

    [TestMethod]
    public async Task UnknownInteraction_IsIgnored()
    {
        await _layout.OnLayoutInteractionAsync(InteractionContext("zoom", "{\"index\":2}"));

        Assert.IsFalse(_context.Variables.TryGet<int>(SparklineLayout.SelectedVariable, out _));
    }

    [TestMethod]
    public async Task SelectPoint_GarbagePayload_DoesNotSetVariable()
    {
        await _layout.OnLayoutInteractionAsync(InteractionContext("select-point", "not-a-number"));

        Assert.IsFalse(_context.Variables.TryGet<int>(SparklineLayout.SelectedVariable, out _));
    }

    // --- Metadata ---

    [TestMethod]
    public void GetLayoutMetadata_IsEmpty()
        => Assert.AreEqual(0, _layout.GetLayoutMetadata().Count);
}
