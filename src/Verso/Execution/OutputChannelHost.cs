using System.Collections.Concurrent;
using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Execution;

/// <summary>
/// How a host carries a channel's traffic to the view rendering it. Implemented once per shell,
/// because there is no mechanism the in-process and out-of-process hosts share: one calls into the
/// page and the other writes a notification down a pipe.
/// </summary>
/// <remarks>
/// Both methods are given a channel id rather than a view handle. Resolving the id to something
/// that can be written to belongs to the shell, which is the only part that knows what a view is.
/// </remarks>
public interface IOutputChannelTransport
{
    /// <summary>
    /// Delivers one message to the channel's view. <paramref name="messageType"/> already carries
    /// its <c>ext/</c> prefix; the shell wraps it rather than renaming it.
    /// </summary>
    Task PostAsync(string channelId, string messageType, object? payload, CancellationToken ct);

    /// <summary>
    /// Tells the channel's view that the channel is gone and it should stop expecting answers.
    /// Called once per channel, and not at session teardown, where the view goes away too.
    /// </summary>
    Task CloseAsync(string channelId, string reason, CancellationToken ct);
}

/// <summary>
/// The session's <see cref="IOutputChannelHost"/>. Owned by <see cref="Scaffold"/> and living as
/// long as it does, which is the whole point: a channel has to still work when no cell is running,
/// because that is when someone is interacting with what the last cell drew.
/// </summary>
public sealed class OutputChannelHost : IOutputChannelHost, IAsyncDisposable
{
    /// <summary>
    /// Messages held for a view that has not mounted. The number is generous for the case it
    /// exists for, an output whose first few messages are produced while the client is still
    /// drawing it, and small enough that a caller writing to a view that will never mount is
    /// stopped rather than left to grow.
    /// </summary>
    public const int MaxQueuedMessages = 256;

    /// <summary>The same bound counted in bytes, for a caller sending few large messages.</summary>
    public const long MaxQueuedBytes = 8L * 1024 * 1024;

    /// <summary>Held for framework messages, so an owner's type can never impersonate one.</summary>
    private const string ReservedPrefix = "verso/";

    /// <summary>What an owner's message type is carried under on the wire.</summary>
    private const string ExtensionPrefix = "ext/";

    private readonly ConcurrentDictionary<string, Channel> _channels = new(StringComparer.Ordinal);
    private volatile bool _disposed;

    /// <summary>
    /// How messages reach a view. Set by the shell that has one; left null by a host that renders
    /// to a file or a terminal.
    /// </summary>
    public IOutputChannelTransport? Transport { get; set; }

    /// <summary>
    /// Whether this host has anywhere to send. What decides whether a context reports channels at
    /// all: a caller that finds <see cref="IVersoContext.OutputChannels"/> null writes static
    /// output, which is the right answer for a host with no view to talk to and a better one than
    /// opening a channel that would queue until it failed.
    /// </summary>
    public bool CanReachViews => Transport is not null;

    // --- IOutputChannelHost ---

    /// <inheritdoc />
    public Task<IOutputChannel> OpenAsync(Guid cellId, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OutputChannelHost));

        ct.ThrowIfCancellationRequested();

        // Random rather than sequential. The id is the only thing standing between a framed
        // document and a channel belonging to a different output, so it should not be guessable
        // from one the frame was already given.
        var channelId = Guid.NewGuid().ToString("N");
        var channel = new Channel(this, channelId, cellId);
        _channels[channelId] = channel;

        return Task.FromResult<IOutputChannel>(channel);
    }

    /// <inheritdoc />
    public IOutputChannel? Find(string channelId)
    {
        if (string.IsNullOrEmpty(channelId))
            return null;

        return _channels.TryGetValue(channelId, out var channel) ? channel : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<IOutputChannel> ForCell(Guid cellId)
        => _channels.Values.Where(c => c.CellId == cellId).ToList();

    // --- Driven by the shell ---

    /// <summary>
    /// A view reporting that it has mounted and can receive. Flushes whatever was queued for it,
    /// in the order it was posted. A view that unmounts and mounts again reports itself ready
    /// again, and a second report on a channel with nothing queued does nothing.
    /// </summary>
    public Task HandleReadyAsync(string channelId, CancellationToken ct = default)
    {
        return _channels.TryGetValue(channelId, out var channel)
            ? channel.FlushAsync(ct)
            : Task.CompletedTask;
    }

    /// <summary>
    /// A message from a view, on its way to the channel's owner. A message naming a channel that
    /// is not open is dropped without complaint: the ordinary way to get one is for a view to
    /// answer a moment after its channel closed.
    /// </summary>
    public Task HandleMessageAsync(string channelId, string messageType, string? payload, IReadOnlyList<byte[]>? buffers = null)
    {
        if (!_channels.TryGetValue(channelId, out var channel))
            return Task.CompletedTask;

        var ownerType = ToOwnerType(messageType);
        if (ownerType is null)
        {
            // Either empty or in the reserved namespace. A framework message is the transport's
            // business and never an owner's, so one arriving here is a transport that failed to
            // handle its own vocabulary rather than something to pass along.
            Report($"A message of type '{messageType}' arrived on a channel and was not delivered, because that type is reserved.");
            return Task.CompletedTask;
        }

        return channel.DeliverAsync(new OutputChannelMessage(channelId, ownerType, payload, buffers));
    }

    /// <summary>Closes one channel and tells its view, if the channel is open.</summary>
    public Task CloseAsync(string channelId, string reason)
    {
        return _channels.TryRemove(channelId, out var channel)
            ? channel.CloseAsync(reason, notifyView: true)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Closes every channel belonging to one cell. What a cell being re-run, cleared, or deleted
    /// comes to.
    /// </summary>
    public async Task CloseForCellAsync(Guid cellId, string reason)
    {
        foreach (var channel in _channels.Values.Where(c => c.CellId == cellId).ToList())
            await CloseAsync(channel.ChannelId, reason).ConfigureAwait(false);
    }

    /// <summary>Closes every open channel. What a kernel restart or a crash comes to.</summary>
    public async Task CloseAllAsync(string reason)
    {
        foreach (var channelId in _channels.Keys.ToList())
            await CloseAsync(channelId, reason).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Closed without telling the views. The session ending takes the surface holding them with
        // it, so there is nothing left to inform and a transport reaching for a torn-down page
        // would only raise on the way out.
        foreach (var channelId in _channels.Keys.ToList())
        {
            if (_channels.TryRemove(channelId, out var channel))
                await channel.CloseAsync("session ended", notifyView: false).ConfigureAwait(false);
        }
    }

    // --- Internals shared with a channel ---

    /// <summary>
    /// Mirrors <c>LayoutFrameChannel.EnsureValidMessageType</c> deliberately. The two guards are
    /// separate because the assemblies do not reference each other in that direction, and they
    /// stay worded alike so an author who has met one recognises the other.
    /// </summary>
    internal static void EnsureValidMessageType(string type)
    {
        if (string.IsNullOrEmpty(type))
            throw new ArgumentException("Message type must be a non-empty string.", nameof(type));

        if (type.StartsWith(ReservedPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Message type '{type}' is in the reserved '{ReservedPrefix}' namespace. " +
                $"Callers must use the '{ExtensionPrefix}'-prefixed namespace (the host applies the prefix automatically).",
                nameof(type));
        }
    }

    /// <summary>
    /// The type an owner sees for a message that arrived, or null when the message is not one an
    /// owner should be given. The prefix is added on the way out and taken off on the way back, so
    /// a round trip carries the type the owner posted rather than the transport's spelling of it.
    /// </summary>
    private static string? ToOwnerType(string messageType)
    {
        if (string.IsNullOrEmpty(messageType))
            return null;

        if (messageType.StartsWith(ReservedPrefix, StringComparison.Ordinal))
            return null;

        return messageType.StartsWith(ExtensionPrefix, StringComparison.Ordinal)
            ? messageType.Substring(ExtensionPrefix.Length)
            : messageType;
    }

    internal Task SendAsync(string channelId, string messageType, object? payload, CancellationToken ct)
    {
        var transport = Transport;

        // A channel only reaches this after a view reported itself ready, which only a transport
        // can report, so a null here is a shell that dropped its transport mid-session.
        return transport is null
            ? Task.CompletedTask
            : transport.PostAsync(channelId, messageType, payload, ct);
    }

    internal async Task NotifyClosedAsync(string channelId, string reason)
    {
        var transport = Transport;
        if (transport is null)
            return;

        try
        {
            await transport.CloseAsync(channelId, reason, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The channel is closed either way. A view that never hears about it stays drawn as it
            // was, which is wrong but not worth failing a teardown over.
            Report($"Could not tell a view that its channel closed: {ex.Message}");
        }
    }

    /// <summary>
    /// How much of the queue's byte bound a payload uses. Serialising to measure costs something,
    /// and it is paid only while a view has not mounted and only up to the bound itself.
    /// </summary>
    private static long MeasurePayload(object? payload)
    {
        if (payload is null)
            return 0;

        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(payload).LongLength;
        }
        catch
        {
            // Measuring is for bounding a queue, not for validating a payload. One that cannot be
            // written still counts against the message bound, and it fails where every other
            // transport failure does.
            return 0;
        }
    }

    private static void Report(string message) => Console.Error.WriteLine($"[Verso] {message}");

    /// <summary>
    /// One channel. Everything a view has not yet asked for is held here rather than on the host,
    /// so a slow view holds up nothing but itself.
    /// </summary>
    private sealed class Channel : IOutputChannel
    {
        private readonly OutputChannelHost _host;
        private readonly object _gate = new();
        private readonly List<QueuedMessage> _queue = new();
        private long _queuedBytes;
        private volatile bool _mounted;
        private volatile bool _isAlive = true;

        public Channel(OutputChannelHost host, string channelId, Guid cellId)
        {
            _host = host;
            ChannelId = channelId;
            CellId = cellId;
        }

        public string ChannelId { get; }

        public Guid CellId { get; }

        public bool IsAlive => _isAlive;

        public event Func<OutputChannelMessage, Task>? MessageReceived;

        public Task PostMessageAsync(string type, object? payload, CancellationToken ct = default)
        {
            EnsureValidMessageType(type);
            EnsureAlive(type);

            var messageType = ExtensionPrefix + type;

            // Measured outside the lock, because measuring can mean writing out a large payload
            // and a channel's other callers should not queue behind that. The flag only ever turns
            // on, so reading it here without the lock can cost a measurement that turns out to be
            // unnecessary and cannot cost one that was needed.
            var size = _mounted ? 0L : MeasurePayload(payload);

            lock (_gate)
            {
                if (!_mounted)
                {
                    if (_queue.Count + 1 > MaxQueuedMessages || _queuedBytes + size > MaxQueuedBytes)
                    {
                        // Deliberately not trimmed to fit. A caller past the bound is talking to a
                        // view that is not going to mount, and dropping the oldest messages would
                        // hand it a conversation with a hole in it instead of an answer.
                        FailOverflow(_queue.Count, _queuedBytes);
                    }
                    else
                    {
                        _queue.Add(new QueuedMessage(messageType, payload));
                        _queuedBytes += size;
                        return Task.CompletedTask;
                    }
                }
            }

            return _host.SendAsync(ChannelId, messageType, payload, ct);
        }

        public ValueTask DisposeAsync() => new(_host.CloseAsync(ChannelId, "disposed by its owner"));

        /// <summary>
        /// Sends what was queued, in order, and only then counts the view as mounted. Setting the
        /// flag last is what keeps a message posted mid-flush behind the ones already waiting:
        /// while anything is queued a new post joins the queue rather than overtaking it.
        /// </summary>
        internal async Task FlushAsync(CancellationToken ct)
        {
            while (_isAlive)
            {
                QueuedMessage[] batch;

                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        _mounted = true;
                        _queuedBytes = 0;
                        return;
                    }

                    batch = _queue.ToArray();
                    _queue.Clear();
                    _queuedBytes = 0;
                }

                foreach (var queued in batch)
                {
                    if (!_isAlive)
                        return;

                    await _host.SendAsync(ChannelId, queued.MessageType, queued.Payload, ct).ConfigureAwait(false);
                }
            }
        }

        internal async Task DeliverAsync(OutputChannelMessage message)
        {
            var handlers = MessageReceived;
            if (handlers is null)
                return;

            foreach (var handler in handlers.GetInvocationList().Cast<Func<OutputChannelMessage, Task>>())
            {
                try
                {
                    await handler(message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // One owner's handler must not stop another's, or the messages after this one.
                    Report($"A handler for a '{message.Type}' message on an output channel failed: {ex.Message}");
                }
            }
        }

        internal Task CloseAsync(string reason, bool notifyView)
        {
            // Turned off before anything else runs, so a handler reached from the notification
            // below already sees a channel it cannot post to.
            _isAlive = false;

            lock (_gate)
            {
                _queue.Clear();
                _queuedBytes = 0;
            }

            return notifyView ? _host.NotifyClosedAsync(ChannelId, reason) : Task.CompletedTask;
        }

        private void EnsureAlive(string type)
        {
            if (_isAlive)
                return;

            throw new InvalidOperationException(
                $"Cannot post message of type '{type}': the output channel '{ChannelId}' is closed.");
        }

        /// <summary>
        /// Closes the channel for holding more than the host will hold, and tells the caller why.
        /// Called while the queue lock is held, so the close is arranged and the throw carries the
        /// numbers that were read under it.
        /// </summary>
        private void FailOverflow(int queuedMessages, long queuedBytes)
        {
            _isAlive = false;
            _queue.Clear();
            _queuedBytes = 0;
            _host._channels.TryRemove(ChannelId, out _);

            Report(
                $"An output channel for cell {CellId} was closed after holding {queuedMessages} messages " +
                $"({queuedBytes} bytes) for a view that never became ready. " +
                $"The limit is {MaxQueuedMessages} messages or {MaxQueuedBytes} bytes.");

            throw new InvalidOperationException(
                $"Cannot post to output channel '{ChannelId}': it has been closed for exceeding " +
                $"the {MaxQueuedMessages} message or {MaxQueuedBytes} byte limit on messages held " +
                $"for a view that has not become ready.");
        }
    }

    private readonly struct QueuedMessage
    {
        public QueuedMessage(string messageType, object? payload)
        {
            MessageType = messageType;
            Payload = payload;
        }

        public string MessageType { get; }

        public object? Payload { get; }
    }
}
