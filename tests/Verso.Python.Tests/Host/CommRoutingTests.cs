using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Verso.Abstractions;
using Verso.Execution;
using Verso.Python.Host;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Covers which views a widget's messages reach, and what a view's answer looks like by the time
/// it reaches the subprocess. A comm belongs to a widget and a channel belongs to a drawn output,
/// and the two do not line up, so deciding between them is worth pinning down on its own.
/// </summary>
[TestClass]
public sealed class CommRoutingTests
{
    private static readonly Guid Cell = Guid.NewGuid();

    /// <summary>Stands in for the shell, recording everything a channel asked it to deliver.</summary>
    private sealed class RecordingTransport : IOutputChannelTransport
    {
        public ConcurrentQueue<(string ChannelId, string MessageType, object? Payload)> Posted { get; } = new();

        public ConcurrentQueue<string> Closed { get; } = new();

        /// <summary>Held before the next post completes, so ordering can be put under pressure.</summary>
        public Func<string, Task>? Delay { get; set; }

        public async Task PostAsync(string channelId, string messageType, object? payload, CancellationToken ct)
        {
            var delay = Delay;
            if (delay is not null)
                await delay(channelId).ConfigureAwait(false);

            Posted.Enqueue((channelId, messageType, payload));
        }

        public Task CloseAsync(string channelId, string reason, CancellationToken ct)
        {
            Closed.Enqueue(channelId);
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public OutputChannelHost Channels { get; } = new();

        public RecordingTransport Transport { get; } = new();

        public ConcurrentQueue<JsonObject> Sent { get; } = new();

        public PythonCommRouter Router { get; }

        public Harness()
        {
            Channels.Transport = Transport;
            Router = new PythonCommRouter(frame =>
            {
                Sent.Enqueue(frame);
                return Task.CompletedTask;
            });
        }

        /// <summary>A channel whose view has mounted, tracked and ready to be written to.</summary>
        public async Task<IOutputChannel> MountedChannelAsync()
        {
            var channel = await Channels.OpenAsync(Cell);
            Router.Track(channel);
            await Channels.HandleReadyAsync(channel.ChannelId);
            return channel;
        }

        public JsonObject Frame(string commId, string? channelId = null, string msgType = "comm_msg")
        {
            var frame = new JsonObject
            {
                ["type"] = "comm",
                ["req_id"] = 0,
                ["comm_id"] = commId,
                ["msg_type"] = msgType,
                ["data"] = new JsonObject { ["method"] = "update" },
                ["buffers"] = new JsonArray(),
            };

            if (channelId is not null)
                frame["channel_id"] = channelId;

            return frame;
        }

        public Task DeliverFromViewAsync(string channelId, JsonObject body, IReadOnlyList<byte[]>? buffers = null)
            => Channels.HandleMessageAsync(channelId, "ext/comm", body.ToJsonString(), buffers);
    }

    private static JsonObject PayloadOf((string ChannelId, string MessageType, object? Payload) post)
        => (JsonObject)post.Payload!;

    // --- subprocess to views ---

    [TestMethod]
    public async Task AWidgetsMessageReachesTheViewDrawingIt()
    {
        var harness = new Harness();
        var channel = await harness.MountedChannelAsync();

        await harness.Router.RouteAsync(harness.Frame("widget-1"));

        Assert.AreEqual(1, harness.Transport.Posted.Count);
        Assert.IsTrue(harness.Transport.Posted.TryDequeue(out var post));
        Assert.AreEqual(channel.ChannelId, post.ChannelId);
        Assert.AreEqual("ext/comm", post.MessageType);
        Assert.AreEqual("widget-1", PayloadOf(post)["comm_id"]!.GetValue<string>());
        Assert.AreEqual("comm_msg", PayloadOf(post)["msg_type"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task WhatTheViewSeesCarriesNothingTheTransportNeeded()
    {
        var harness = new Harness();
        var channel = await harness.MountedChannelAsync();

        await harness.Router.RouteAsync(harness.Frame("widget-1", channel.ChannelId));

        Assert.IsTrue(harness.Transport.Posted.TryDequeue(out var post));
        var payload = PayloadOf(post);

        // The envelope belongs to the pipe the message came down. A view is given its own
        // channel's traffic and nothing else, so repeating any of this would tell it nothing.
        Assert.IsFalse(payload.ContainsKey("type"));
        Assert.IsFalse(payload.ContainsKey("req_id"));
        Assert.IsFalse(payload.ContainsKey("channel_id"));
        Assert.IsTrue(payload.ContainsKey("data"));
    }

    [TestMethod]
    public async Task AnAnswerGoesOnlyToTheViewThatAskedForIt()
    {
        var harness = new Harness();
        var asking = await harness.MountedChannelAsync();
        var other = await harness.MountedChannelAsync();

        await harness.Router.RouteAsync(harness.Frame("widget-1", asking.ChannelId));

        Assert.AreEqual(1, harness.Transport.Posted.Count);
        Assert.IsTrue(harness.Transport.Posted.TryDequeue(out var post));
        Assert.AreEqual(asking.ChannelId, post.ChannelId);
        Assert.AreNotEqual(other.ChannelId, post.ChannelId);
    }

    [TestMethod]
    public async Task AnUnaddressedMessageGoesToTheViewsAlreadyHoldingThatWidget()
    {
        var harness = new Harness();
        var holding = await harness.MountedChannelAsync();
        await harness.MountedChannelAsync();

        // Answering this view is how the router learns which widget it holds.
        await harness.Router.RouteAsync(harness.Frame("widget-1", holding.ChannelId));
        harness.Transport.Posted.Clear();

        // A trait assigned in a cell names no channel, and only one view can draw the result.
        await harness.Router.RouteAsync(harness.Frame("widget-1"));

        Assert.AreEqual(1, harness.Transport.Posted.Count);
        Assert.IsTrue(harness.Transport.Posted.TryDequeue(out var post));
        Assert.AreEqual(holding.ChannelId, post.ChannelId);
    }

    [TestMethod]
    public async Task AWidgetNoViewHoldsYetIsOfferedToEveryView()
    {
        var harness = new Harness();
        var first = await harness.MountedChannelAsync();
        var second = await harness.MountedChannelAsync();

        // A widget's first message is its own opening, sent while it is being constructed and
        // before anything has been drawn. Withholding it would leave every view without a model.
        await harness.Router.RouteAsync(harness.Frame("widget-1", msgType: "comm_open"));

        var reached = harness.Transport.Posted.Select(p => p.ChannelId).ToList();
        CollectionAssert.AreEquivalent(new[] { first.ChannelId, second.ChannelId }, reached);
    }

    [TestMethod]
    public async Task AViewThatHasGoneStopsBeingWrittenTo()
    {
        var harness = new Harness();
        var channel = await harness.MountedChannelAsync();

        await channel.DisposeAsync();
        await harness.Router.RouteAsync(harness.Frame("widget-1"));

        Assert.AreEqual(0, harness.Transport.Posted.Count(p => p.MessageType == "ext/comm"));
    }

    [TestMethod]
    public async Task MessagesReachAViewInTheOrderTheyWereRaised()
    {
        var harness = new Harness();
        await harness.MountedChannelAsync();

        // The first delivery is held open, which is what a round trip to a browser does. Without
        // ordering, the second would overtake it and the widget would settle on the older value.
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        harness.Transport.Delay = async _ =>
        {
            if (!first) return;
            first = false;
            await held.Task;
        };

        var one = harness.Router.RouteAsync(harness.Frame("widget-1"));
        var two = harness.Router.RouteAsync(harness.Frame("widget-2"));

        held.SetResult();
        await Task.WhenAll(one, two);

        var order = harness.Transport.Posted
            .Select(p => PayloadOf(p)["comm_id"]!.GetValue<string>())
            .ToList();

        CollectionAssert.AreEqual(new[] { "widget-1", "widget-2" }, order);
    }

    [TestMethod]
    public async Task AWidgetWithNoIdentityIsNotDelivered()
    {
        var harness = new Harness();
        await harness.MountedChannelAsync();

        var frame = harness.Frame("widget-1");
        frame.Remove("comm_id");

        await harness.Router.RouteAsync(frame);

        Assert.AreEqual(0, harness.Transport.Posted.Count);
    }

    // --- views to subprocess ---

    [TestMethod]
    public async Task AViewsMessageBecomesAFrameTheSubprocessCanRead()
    {
        var harness = new Harness();
        var channel = await harness.MountedChannelAsync();

        await harness.DeliverFromViewAsync(channel.ChannelId, new JsonObject
        {
            ["comm_id"] = "widget-1",
            ["data"] = new JsonObject { ["method"] = "update", ["state"] = new JsonObject { ["value"] = 42 } },
        });

        Assert.IsTrue(harness.Sent.TryDequeue(out var frame));
        Assert.AreEqual("comm_msg", frame!["type"]!.GetValue<string>());
        Assert.AreEqual(channel.ChannelId, frame["channel_id"]!.GetValue<string>());
        Assert.AreEqual("widget-1", frame["comm_id"]!.GetValue<string>());
        Assert.AreEqual("comm_msg", frame["msg_type"]!.GetValue<string>());
        Assert.AreEqual(42, frame["data"]!["state"]!["value"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task AViewIsTheOneWhoSaysWhichChannelItsMessageCameFrom()
    {
        var harness = new Harness();
        var channel = await harness.MountedChannelAsync();

        // A view naming somebody else's channel is not taken at its word: the id on the frame is
        // the one the message actually arrived on.
        await harness.DeliverFromViewAsync(channel.ChannelId, new JsonObject
        {
            ["comm_id"] = "widget-1",
            ["channel_id"] = "a-channel-belonging-to-someone-else",
            ["data"] = new JsonObject(),
        });

        Assert.IsTrue(harness.Sent.TryDequeue(out var frame));
        Assert.AreEqual(channel.ChannelId, frame!["channel_id"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task BinaryPartsFromAViewArriveAsBase64()
    {
        var harness = new Harness();
        var channel = await harness.MountedChannelAsync();

        // Carried in the body, which is where a view that speaks this protocol already puts them.
        await harness.DeliverFromViewAsync(channel.ChannelId, new JsonObject
        {
            ["comm_id"] = "widget-1",
            ["buffers"] = new JsonArray("AQID"),
        });

        Assert.IsTrue(harness.Sent.TryDequeue(out var fromBody));
        Assert.AreEqual("AQID", fromBody!["buffers"]![0]!.GetValue<string>());

        // And carried alongside it, which is what the transport's own slot is for. Either way
        // the subprocess reads one thing.
        await harness.DeliverFromViewAsync(
            channel.ChannelId,
            new JsonObject { ["comm_id"] = "widget-1" },
            new[] { new byte[] { 1, 2, 3 } });

        Assert.IsTrue(harness.Sent.TryDequeue(out var fromSlot));
        Assert.AreEqual("AQID", fromSlot!["buffers"]![0]!.GetValue<string>());
    }

    [TestMethod]
    public async Task AMessageFromAViewThatNamesNoWidgetIsIgnored()
    {
        var harness = new Harness();
        var channel = await harness.MountedChannelAsync();

        await harness.DeliverFromViewAsync(
            channel.ChannelId, new JsonObject { ["data"] = new JsonObject() });

        Assert.AreEqual(0, harness.Sent.Count);
    }

    [TestMethod]
    public async Task SomethingOtherThanCommTrafficOnTheSameChannelIsLeftAlone()
    {
        var harness = new Harness();
        var channel = await harness.MountedChannelAsync();

        // A channel can carry an owner's own vocabulary as well. Answering everything on it
        // would put messages in front of the subprocess that were never meant for a widget.
        await harness.Channels.HandleMessageAsync(
            channel.ChannelId, "ext/something-else", "{\"comm_id\":\"widget-1\"}");

        Assert.AreEqual(0, harness.Sent.Count);
    }

    [TestMethod]
    public async Task AViewGoingAwayLeavesTheWidgetItWasDrawingAlone()
    {
        var harness = new Harness();
        var channel = await harness.MountedChannelAsync();

        await harness.Router.RouteAsync(harness.Frame("widget-1", channel.ChannelId));
        await harness.Channels.CloseAsync(channel.ChannelId, "the cell was re-run");

        // Nothing was sent to the subprocess about the comm. A widget outlives the view drawing
        // it: the ordinary way to lose a view is to re-run the cell, which then displays the
        // same widget again, and closing its comm would leave it drawable nowhere.
        Assert.AreEqual(0, harness.Sent.Count);

        // And the widget's later messages have nowhere to go rather than going somewhere wrong.
        harness.Transport.Posted.Clear();
        await harness.Router.RouteAsync(harness.Frame("widget-1"));
        Assert.AreEqual(0, harness.Transport.Posted.Count);
    }

    [TestMethod]
    public async Task ClearingStopsEveryChannelAtOnce()
    {
        var harness = new Harness();
        var channel = await harness.MountedChannelAsync();

        harness.Router.Clear();
        await harness.Router.RouteAsync(harness.Frame("widget-1"));

        Assert.AreEqual(0, harness.Transport.Posted.Count);

        // And the channel itself is untouched, because a restart is not a reason to tell a view
        // something its own host has not decided yet.
        Assert.IsTrue(channel.IsAlive);
    }
}
