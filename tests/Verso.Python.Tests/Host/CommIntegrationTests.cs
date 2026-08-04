using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Verso.Abstractions;
using Verso.Execution;
using Verso.Python.Host;
using Verso.Python.Interpreter;
using Verso.Python.Kernel;
using Verso.Python.Tests.Interpreter;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Covers a widget and a stand-in for the view drawing it, talking to each other through a real
/// interpreter. The halves are pinned down separately elsewhere; what this adds is that they meet.
/// </summary>
/// <remarks>
/// Nothing installs ipywidgets for this, for the reason the embedding tests give: it is the
/// author's own dependency and it cannot be added to the environment the rest of the suite runs
/// in. An environment without it reports inconclusive.
/// </remarks>
[TestClass]
public sealed class CommIntegrationTests
{
    private static string Python => PythonProbe.Require();

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private sealed class RecordingTransport : IOutputChannelTransport
    {
        public ConcurrentQueue<JsonObject> Posted { get; } = new();

        public Task PostAsync(string channelId, string messageType, object? payload, CancellationToken ct)
        {
            if (payload is JsonObject body)
                Posted.Enqueue(body);

            return Task.CompletedTask;
        }

        public Task CloseAsync(string channelId, string reason, CancellationToken ct) => Task.CompletedTask;
    }

    private static async Task<PythonHostSession> StartedSessionAsync()
    {
        var python = Python;
        var validator = new FakeInterpreterValidator().Valid(python, version: "3.13.0");
        var locator = new InterpreterLocator(validator, _ => null, () => Array.Empty<string>());

        var session = new PythonHostSession(new PythonKernelOptions { PythonExecutable = python }, locator: locator);
        await session.StartAsync(CancellationToken.None);
        return session;
    }

    private static async Task RequireWidgetsAsync(PythonHostSession session, StubExecutionContext context)
    {
        var probe = await session.ExecuteAsync("import ipywidgets", context);
        if (probe.Any(o => o.IsError))
            Assert.Inconclusive("ipywidgets is not importable in this environment.");
    }

    /// <summary>
    /// A context whose outputs can reach a view, which is what makes a widget live. Nothing here
    /// stands in for the session: the channel a widget gets is one the session opened for it.
    /// </summary>
    private static (StubExecutionContext Context, OutputChannelHost Channels, RecordingTransport Transport) LiveContext()
    {
        var channels = new OutputChannelHost();
        var transport = new RecordingTransport();
        channels.Transport = transport;

        var context = new StubExecutionContext { OutputChannels = channels };
        return (context, channels, transport);
    }

    /// <summary>The channel the session opened for a widget the cell drew.</summary>
    private static string ChannelOf(IEnumerable<CellOutput> outputs)
    {
        var widget = outputs.FirstOrDefault(o => o.MimeType == CellOutput.WidgetMimeType);
        Assert.IsNotNull(widget, "The cell produced no widget output.");
        Assert.IsFalse(
            string.IsNullOrEmpty(widget!.LiveChannelId),
            "The widget was drawn without a channel, so nothing can reach it.");

        return widget.LiveChannelId!;
    }

    private static string TextOf(IEnumerable<CellOutput> outputs)
        => string.Concat(outputs.Select(o => o.Content));

    /// <summary>
    /// Waits for a frame the widget produced. Frames are delivered from the read pump rather than
    /// as part of the reply, so a cell returning is not by itself evidence that one has arrived.
    /// </summary>
    private static async Task<JsonObject> WaitForAsync(RecordingTransport transport, Func<JsonObject, bool> matches)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            var found = transport.Posted.FirstOrDefault(matches);
            if (found is not null)
                return found;

            await Task.Delay(50);
        }

        Assert.Fail("No matching widget message arrived within the time allowed.");
        return null!;
    }

    [TestMethod]
    [Timeout(180_000)]
    public async Task AWidgetAndItsViewTalkToEachOther()
    {
        await using var session = await StartedSessionAsync();
        var (context, channels, transport) = LiveContext();
        await RequireWidgetsAsync(session, context);

        // Every observed value, so a change applied twice is as visible as one never applied.
        var setup = await session.ExecuteAsync(@"
import ipywidgets

seen = []
slider = ipywidgets.IntSlider(value=3)
slider.observe(lambda change: seen.append(change['new']), names='value')
slider
", context);

        Assert.IsFalse(setup.Any(o => o.IsError), $"Building the widget failed: {TextOf(setup)}");

        // Displaying the widget is what gave it a channel, and the channel travels on the output
        // rather than inside the document, so a saved file names nothing that has since gone.
        var channelId = ChannelOf(setup);
        await channels.HandleReadyAsync(channelId, "1.0");

        var modelId = await ReadAsync(session, context, "slider.model_id");

        // The view asks for the state of every widget as it mounts, which is what covers a widget
        // built before anything was drawing it. Nothing else would have told this view it exists.
        await channels.HandleMessageAsync(channelId, "ext/comm", new JsonObject
        {
            ["comm_id"] = "control-comm",
            ["msg_type"] = "comm_open",
            ["target_name"] = "jupyter.widget.control",
            ["metadata"] = new JsonObject { ["version"] = "1.0.0" },
            ["data"] = new JsonObject(),
        }.ToJsonString());

        await channels.HandleMessageAsync(channelId, "ext/comm", new JsonObject
        {
            ["comm_id"] = "control-comm",
            ["data"] = new JsonObject { ["method"] = "request_states" },
        }.ToJsonString());

        var states = await WaitForAsync(
            transport,
            f => f["comm_id"]?.GetValue<string>() == "control-comm"
                 && f["data"]?["method"]?.GetValue<string>() == "update_states");

        Assert.AreEqual(
            3, states["data"]!["states"]![modelId]!["state"]!["value"]!.GetValue<int>(),
            "The state the view was given is not what Python holds.");

        // And now the other way: the view says the slider moved.
        await channels.HandleMessageAsync(channelId, "ext/comm", new JsonObject
        {
            ["comm_id"] = modelId,
            ["data"] = new JsonObject
            {
                ["method"] = "update",
                ["state"] = new JsonObject { ["value"] = 42 },
            },
        }.ToJsonString());

        var applied = await session.ExecuteAsync("(slider.value, seen)", context);

        Assert.IsFalse(applied.Any(o => o.IsError), $"Reading the widget back failed: {TextOf(applied)}");
        Assert.AreEqual("(42, [42])", TextOf(applied).Trim());
    }

    [TestMethod]
    [Timeout(180_000)]
    public async Task ATraitSetInACellReachesTheViewAfterTheCellThatSetIt()
    {
        await using var session = await StartedSessionAsync();
        var (context, channels, transport) = LiveContext();
        await RequireWidgetsAsync(session, context);

        var setup = await session.ExecuteAsync(
            "import ipywidgets\nslider = ipywidgets.IntSlider(value=3)\nslider", context);

        await channels.HandleReadyAsync(ChannelOf(setup), "1.0");
        var modelId = await ReadAsync(session, context, "slider.model_id");

        var moved = await session.ExecuteAsync("slider.value = 17", context);
        Assert.IsFalse(moved.Any(o => o.IsError), $"Moving the slider failed: {TextOf(moved)}");

        var update = await WaitForAsync(
            transport,
            f => f["comm_id"]?.GetValue<string>() == modelId
                 && f["msg_type"]?.GetValue<string>() == "comm_msg"
                 && f["data"]?["state"]?["value"]?.GetValue<int>() == 17);

        Assert.AreEqual("update", update["data"]!["method"]!.GetValue<string>());
    }

    [TestMethod]
    [Timeout(180_000)]
    public async Task AViewIsToldEachOfItsMessagesLandedSoItKeepsSending()
    {
        await using var session = await StartedSessionAsync();
        var (context, channels, transport) = LiveContext();
        await RequireWidgetsAsync(session, context);

        var setup = await session.ExecuteAsync(@"
import ipywidgets

seen = []
slider = ipywidgets.IntSlider(value=0)
slider.observe(lambda change: seen.append(change['new']), names='value')
slider
", context);

        var channelId = ChannelOf(setup);
        await channels.HandleReadyAsync(channelId, "1.0");
        var modelId = await ReadAsync(session, context, "slider.model_id");

        // A front end holds every change after the one in flight until it hears that one landed,
        // so without the answer a widget goes quiet after a single interaction.
        for (var value = 1; value <= 3; value++)
        {
            await channels.HandleMessageAsync(channelId, "ext/comm", new JsonObject
            {
                ["comm_id"] = modelId,
                ["msg_id"] = $"from-the-view-{value}",
                ["data"] = new JsonObject
                {
                    ["method"] = "update",
                    ["state"] = new JsonObject { ["value"] = value },
                },
            }.ToJsonString());

            await WaitForAsync(
                transport,
                f => f["msg_type"]?.GetValue<string>() == "comm_ack"
                     && f["msg_id"]?.GetValue<string>() == $"from-the-view-{value}");
        }

        var applied = await session.ExecuteAsync("(slider.value, seen)", context);

        Assert.IsFalse(applied.Any(o => o.IsError), $"Reading the widget back failed: {TextOf(applied)}");
        Assert.AreEqual("(3, [1, 2, 3])", TextOf(applied).Trim());
    }

    [TestMethod]
    [Timeout(180_000)]
    public async Task AWidgetDrawnWhereNothingCanReachItIsStillDrawn()
    {
        await using var session = await StartedSessionAsync();

        // No channel host, which is what baking a file and running a terminal both look like.
        var context = new StubExecutionContext();
        await RequireWidgetsAsync(session, context);

        var outputs = await session.ExecuteAsync(
            "import ipywidgets\nipywidgets.IntSlider(value=3)", context);

        var widget = outputs.FirstOrDefault(o => o.MimeType == CellOutput.WidgetMimeType);
        Assert.IsNotNull(widget, "The cell produced no widget output.");
        Assert.IsNull(widget!.LiveChannelId);

        // The same document either way, carrying the state that lets it draw itself with nobody
        // to talk to. That is what a saved file holds and what an exported page shows.
        StringAssert.Contains(widget.Content, "application/vnd.jupyter.widget-state+json");
        StringAssert.Contains(widget.Content, "application/vnd.jupyter.widget-view+json");
    }

    private static async Task<string> ReadAsync(
        PythonHostSession session, StubExecutionContext context, string expression)
    {
        var outputs = await session.ExecuteAsync(expression, context);
        Assert.IsFalse(outputs.Any(o => o.IsError), $"Reading '{expression}' failed: {TextOf(outputs)}");
        return TextOf(outputs).Trim().Trim('\'');
    }
}
