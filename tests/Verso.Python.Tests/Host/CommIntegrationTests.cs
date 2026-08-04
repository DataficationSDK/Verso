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

    private static async Task RequireWidgetsAsync(PythonHostSession session)
    {
        var probe = await session.ExecuteAsync("import ipywidgets", new StubExecutionContext());
        if (probe.Any(o => o.IsError))
            Assert.Inconclusive("ipywidgets is not importable in this environment.");
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
        await RequireWidgetsAsync(session);

        var channels = new OutputChannelHost();
        var transport = new RecordingTransport();
        channels.Transport = transport;

        var channel = await channels.OpenAsync(Guid.NewGuid());
        session.TrackCommChannel(channel);
        await channels.HandleReadyAsync(channel.ChannelId, "1.0");

        // Every observed value, so a change applied twice is as visible as one never applied.
        var setup = await session.ExecuteAsync(@"
import ipywidgets

seen = []
slider = ipywidgets.IntSlider(value=3)
slider.observe(lambda change: seen.append(change['new']), names='value')
slider.model_id
", new StubExecutionContext());

        Assert.IsFalse(setup.Any(o => o.IsError), $"Building the widget failed: {TextOf(setup)}");

        var modelId = TextOf(setup).Trim().Trim('\'');

        // The widget announced itself to the view without anything asking it to, which is the
        // direction that has no request behind it and no cell running by the time it matters.
        var opened = await WaitForAsync(
            transport,
            f => f["comm_id"]?.GetValue<string>() == modelId
                 && f["msg_type"]?.GetValue<string>() == "comm_open");

        Assert.AreEqual("jupyter.widget", opened["target_name"]!.GetValue<string>());
        Assert.AreEqual(3, opened["data"]!["state"]!["value"]!.GetValue<int>());

        // And now the other way: the view says the slider moved.
        var fromView = new JsonObject
        {
            ["comm_id"] = modelId,
            ["data"] = new JsonObject
            {
                ["method"] = "update",
                ["state"] = new JsonObject { ["value"] = 42 },
            },
        };

        await channels.HandleMessageAsync(channel.ChannelId, "ext/comm", fromView.ToJsonString());

        var applied = await session.ExecuteAsync("(slider.value, seen)", new StubExecutionContext());

        Assert.IsFalse(applied.Any(o => o.IsError), $"Reading the widget back failed: {TextOf(applied)}");
        Assert.AreEqual("(42, [42])", TextOf(applied).Trim());
    }

    [TestMethod]
    [Timeout(180_000)]
    public async Task ATraitSetInACellReachesTheViewAfterTheCellThatSetIt()
    {
        await using var session = await StartedSessionAsync();
        await RequireWidgetsAsync(session);

        var channels = new OutputChannelHost();
        var transport = new RecordingTransport();
        channels.Transport = transport;

        var channel = await channels.OpenAsync(Guid.NewGuid());
        session.TrackCommChannel(channel);
        await channels.HandleReadyAsync(channel.ChannelId, "1.0");

        var setup = await session.ExecuteAsync(
            "import ipywidgets\nslider = ipywidgets.IntSlider(value=3)\nslider.model_id",
            new StubExecutionContext());

        var modelId = TextOf(setup).Trim().Trim('\'');
        await WaitForAsync(transport, f => f["comm_id"]?.GetValue<string>() == modelId);

        var moved = await session.ExecuteAsync("slider.value = 17", new StubExecutionContext());
        Assert.IsFalse(moved.Any(o => o.IsError), $"Moving the slider failed: {TextOf(moved)}");

        var update = await WaitForAsync(
            transport,
            f => f["comm_id"]?.GetValue<string>() == modelId
                 && f["msg_type"]?.GetValue<string>() == "comm_msg"
                 && f["data"]?["state"]?["value"]?.GetValue<int>() == 17);

        Assert.AreEqual("update", update["data"]!["method"]!.GetValue<string>());
    }
}
