using System.Text.Json;
using System.Text.Json.Nodes;
using Verso.Abstractions;

namespace Verso.Python.Host;

/// <summary>
/// Carries widget comm traffic between the subprocess and the views drawing its widgets.
/// </summary>
/// <remarks>
/// A comm belongs to a widget model and a channel belongs to one drawn output, and the two do not
/// line up: one widget can be drawn in several places, and one place draws many widgets. Deciding
/// which views a message reaches is the whole of this class, and it is kept apart from the session
/// so it can be exercised without a subprocess to produce the messages.
/// <para>
/// The body of a message is never read. Field names are the subprocess's own on the way out and
/// the view's on the way back, so what an author's widget sends is what its front end receives,
/// with nothing in between having an opinion about the shape of it.
/// </para>
/// </remarks>
internal sealed class PythonCommRouter
{
    /// <summary>
    /// What comm traffic is called on a channel. One type for all three comm messages, with which
    /// one it is inside the body, so a message passes through as it arrived.
    /// </summary>
    internal const string ChannelMessageType = "comm";

    private readonly Func<JsonObject, Task> _send;
    private readonly object _gate = new();
    private readonly List<Tracked> _tracked = new();

    /// <summary>What every frame so far has been delivered behind. Read and replaced under the gate.</summary>
    private Task _ordered = Task.CompletedTask;

    public PythonCommRouter(Func<JsonObject, Task> send)
        => _send = send ?? throw new ArgumentNullException(nameof(send));

    /// <summary>
    /// Starts carrying comm traffic over a channel, and starts listening for what its view sends
    /// back. Tracking the same channel twice does nothing the first call did not already do.
    /// </summary>
    public void Track(IOutputChannel channel)
    {
        if (channel is null)
            throw new ArgumentNullException(nameof(channel));

        lock (_gate)
        {
            if (_tracked.Any(t => ReferenceEquals(t.Channel, channel)))
                return;

            _tracked.Add(new Tracked(channel));
        }

        channel.MessageReceived += OnChannelMessageAsync;
    }

    /// <summary>Stops carrying traffic over every channel. What a restart or a crash comes to.</summary>
    public void Clear()
    {
        List<Tracked> dropped;
        lock (_gate)
        {
            dropped = _tracked.ToList();
            _tracked.Clear();
        }

        foreach (var entry in dropped)
            entry.Channel.MessageReceived -= OnChannelMessageAsync;
    }

    // --- subprocess to views ---

    /// <summary>
    /// Delivers one <c>comm</c> frame to the views that should see it.
    /// </summary>
    /// <remarks>
    /// A frame naming a channel is an answer to something that view asked, and goes only there.
    /// A frame naming none was raised by a trait changing in a cell, so it goes to every view
    /// already holding that widget's model, and to every view at all when no view holds it yet,
    /// which is how a model first reaches the front ends that will need it.
    /// </remarks>
    public Task RouteAsync(JsonObject frame)
    {
        lock (_gate)
        {
            // Each frame waits for the one before it. Delivery reaches a browser and takes as
            // long as that does, so frames handed over without waiting would otherwise arrive in
            // whatever order their round trips happened to finish in, and a widget's state is a
            // sequence of changes rather than a set of them.
            _ordered = _ordered.ContinueWith(
                _ => DeliverAsync(frame), TaskScheduler.Default).Unwrap();
            return _ordered;
        }
    }

    private async Task DeliverAsync(JsonObject frame)
    {
        var commId = HostProtocol.TryGetString(frame, HostProtocol.CommIdField);
        if (string.IsNullOrEmpty(commId))
            return;

        var channelId = HostProtocol.TryGetString(frame, HostProtocol.ChannelIdField);
        var payload = BuildPayload(frame);

        foreach (var entry in Recipients(channelId, commId!))
        {
            entry.Remember(commId!);

            try
            {
                await entry.Channel.PostMessageAsync(ChannelMessageType, payload).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The channel closed between being chosen and being written to, which is what a
                // view going away while its widget is mid-update looks like from here.
                Untrack(entry);
            }
            catch (Exception ex)
            {
                // One view that cannot be reached must not stop the others, and must not stop
                // the frames behind this one either: every frame is delivered behind this task.
                Console.Error.WriteLine(
                    $"[Verso] A widget message could not be delivered to a view: {ex.Message}");
            }
        }
    }

    private List<Tracked> Recipients(string? channelId, string commId)
    {
        lock (_gate)
        {
            PruneClosed();

            if (!string.IsNullOrEmpty(channelId))
            {
                var named = _tracked.FirstOrDefault(t => t.Channel.ChannelId == channelId);
                return named is null ? new List<Tracked>() : new List<Tracked> { named };
            }

            var holders = _tracked.Where(t => t.Holds(commId)).ToList();
            return holders.Count > 0 ? holders : _tracked.ToList();
        }
    }

    /// <summary>
    /// The frame as a view sees it: everything except what the transport underneath needed to
    /// carry it. The channel it arrived for is dropped too, since a view is only ever given its
    /// own channel's traffic and repeating the id would tell it nothing it does not know.
    /// </summary>
    private static JsonObject BuildPayload(JsonObject frame)
    {
        var payload = new JsonObject();

        foreach (var property in frame)
        {
            if (property.Key is HostProtocol.TypeField
                or HostProtocol.ReqIdField
                or HostProtocol.ChannelIdField)
            {
                continue;
            }

            payload[property.Key] = property.Value?.DeepClone();
        }

        return payload;
    }

    // --- views to subprocess ---

    private Task OnChannelMessageAsync(OutputChannelMessage message)
    {
        if (!string.Equals(message.Type, ChannelMessageType, StringComparison.Ordinal))
            return Task.CompletedTask;

        JsonObject body;
        try
        {
            body = message.Payload is null
                ? new JsonObject()
                : JsonNode.Parse(message.Payload) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            // A view sending something that is not a comm message at all. Nothing downstream
            // could route it, and the view is the only part that could put it right.
            return Task.CompletedTask;
        }

        var commId = HostProtocol.TryGetString(body, HostProtocol.CommIdField);
        if (string.IsNullOrEmpty(commId))
            return Task.CompletedTask;

        RememberOn(message.ChannelId, commId!);

        var frame = new JsonObject
        {
            [HostProtocol.TypeField] = HostProtocol.CommMsg,
            [HostProtocol.ChannelIdField] = message.ChannelId,
            [HostProtocol.CommIdField] = commId,
            [HostProtocol.MsgTypeField] =
                HostProtocol.TryGetString(body, HostProtocol.MsgTypeField) ?? "comm_msg",
            [HostProtocol.DataField] = body[HostProtocol.DataField]?.DeepClone(),
            [HostProtocol.MetadataField] = body[HostProtocol.MetadataField]?.DeepClone(),
            [HostProtocol.TargetNameField] = body[HostProtocol.TargetNameField]?.DeepClone(),
            [HostProtocol.BuffersField] = BuffersFor(body, message.Buffers),
        };

        return _send(frame);
    }

    /// <summary>
    /// A message's binary parts as base64 text, which is the form the subprocess reads.
    /// </summary>
    /// <remarks>
    /// Taken from the body when they are there, because that is where they already are as text
    /// and moving them costs nothing. The transport's own slot is the fallback, and its contents
    /// have been decoded on the way in, so using it means encoding them again.
    /// </remarks>
    private static JsonArray BuffersFor(JsonObject body, IReadOnlyList<byte[]>? transportBuffers)
    {
        if (body[HostProtocol.BuffersField] is JsonArray fromBody)
            return (JsonArray)fromBody.DeepClone();

        var buffers = new JsonArray();
        if (transportBuffers is null)
            return buffers;

        foreach (var buffer in transportBuffers)
            buffers.Add(Convert.ToBase64String(buffer));

        return buffers;
    }

    // --- bookkeeping ---

    private void RememberOn(string channelId, string commId)
    {
        lock (_gate)
        {
            PruneClosed();
            _tracked.FirstOrDefault(t => t.Channel.ChannelId == channelId)?.Remember(commId);
        }
    }

    private void Untrack(Tracked entry)
    {
        lock (_gate)
        {
            if (!_tracked.Remove(entry))
                return;
        }

        entry.Channel.MessageReceived -= OnChannelMessageAsync;
    }

    /// <summary>
    /// Drops the channels whose views have gone, and with them the record of which widgets those
    /// views held. The widgets themselves are left alone: a cell that is re-run displays the same
    /// widget again, and closing its comm would leave the author holding one that can no longer
    /// be drawn anywhere.
    /// </summary>
    private void PruneClosed()
    {
        for (var i = _tracked.Count - 1; i >= 0; i--)
        {
            if (_tracked[i].Channel.IsAlive)
                continue;

            var dropped = _tracked[i];
            _tracked.RemoveAt(i);
            dropped.Channel.MessageReceived -= OnChannelMessageAsync;
        }
    }

    /// <summary>One channel and the widget models its view is known to hold.</summary>
    private sealed class Tracked
    {
        private readonly HashSet<string> _comms = new(StringComparer.Ordinal);

        public Tracked(IOutputChannel channel) => Channel = channel;

        public IOutputChannel Channel { get; }

        public bool Holds(string commId)
        {
            lock (_comms)
                return _comms.Contains(commId);
        }

        public void Remember(string commId)
        {
            lock (_comms)
                _comms.Add(commId);
        }
    }
}
