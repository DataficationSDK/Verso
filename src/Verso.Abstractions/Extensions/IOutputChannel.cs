namespace Verso.Abstractions;

/// <summary>
/// A two-way message channel between one cell output and the view that renders it, held for as
/// long as the view is mounted rather than for the length of a cell execution.
/// </summary>
/// <remarks>
/// Deliberately shaped like <see cref="ILayoutFrameChannel"/>, which solves the same problem one
/// layer up: a component that has to keep talking to something drawn on a page it cannot reach
/// directly. The difference is lifetime. A layout frame channel belongs to one mount of one
/// renderer, and this one belongs to one output for as long as the session holds it, so it is
/// still usable when no cell is running, which is exactly when someone is interacting with what
/// the last cell drew.
/// </remarks>
public interface IOutputChannel : IAsyncDisposable
{
    /// <summary>
    /// Host-allocated, opaque token identifying this channel. Handed only to the view the channel
    /// belongs to, so a message naming a channel is a claim the host checks rather than trusts.
    /// </summary>
    string ChannelId { get; }

    /// <summary>The cell whose output this channel belongs to.</summary>
    Guid CellId { get; }

    /// <summary>
    /// Whether the channel is still open. Turns <c>false</c> before anything else observes the
    /// close, so a handler running at close time already sees it. Once it is <c>false</c>,
    /// <see cref="PostMessageAsync"/> throws rather than dropping the message, which is what a
    /// caller posting from a callback able to outlive the view needs: a variable-changed handler
    /// racing the close is the case this is written for.
    /// </summary>
    bool IsAlive { get; }

    /// <summary>
    /// Posts a typed message to the view. The host prefixes the supplied <paramref name="type"/>
    /// with <c>"ext/"</c> before delivery, and strips the same prefix from what comes back, so a
    /// round trip carries the type the caller supplied.
    /// </summary>
    /// <remarks>
    /// A dead channel or an invalid <paramref name="type"/> is never silently dropped: both throw
    /// synchronously, before the returned task is awaited. Guard with <see cref="IsAlive"/> when
    /// posting from a callback that can run after the view has gone.
    ///
    /// Posting before the view has mounted is ordinary and is what happens every time, because an
    /// output's first message is produced while the client is still rendering it. Those messages
    /// are queued in order and delivered when the view reports itself ready.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="type"/> is null or empty, or is in the reserved <c>verso/</c> namespace,
    /// which is held for framework messages (the host adds the <c>ext/</c> prefix automatically,
    /// so callers never supply it).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The channel is closed (<see cref="IsAlive"/> is <c>false</c>). A host closes a channel when
    /// its view goes away, and also when a caller posts more than the host will hold for a view
    /// that has not mounted.
    /// </exception>
    Task PostMessageAsync(string type, object? payload, CancellationToken ct = default);

    /// <summary>
    /// Raised for each message the view sends back. Handlers are awaited in turn, and one that
    /// throws is reported and does not stop the handlers after it or the messages after this one.
    /// </summary>
    event Func<OutputChannelMessage, Task>? MessageReceived;
}

/// <summary>
/// Session-scoped owner of every open <see cref="IOutputChannel"/>. Reached from
/// <see cref="IVersoContext.OutputChannels"/>, where <c>null</c> means the host cannot reach a
/// rendered view and a caller should produce static output instead.
/// </summary>
public interface IOutputChannelHost
{
    /// <summary>
    /// Opens a channel for an output the caller is about to write. The channel is usable
    /// immediately; messages posted before the view mounts and reports itself ready are queued and
    /// delivered in order once it does.
    /// </summary>
    Task<IOutputChannel> OpenAsync(Guid cellId, CancellationToken ct = default);

    /// <summary>Finds an open channel by id, or <c>null</c> if there is none by that id.</summary>
    IOutputChannel? Find(string channelId);

    /// <summary>Every channel currently open for a cell.</summary>
    IReadOnlyList<IOutputChannel> ForCell(Guid cellId);
}

/// <summary>
/// One message from a view to the owner of its channel.
/// </summary>
/// <param name="ChannelId">The channel the message arrived on.</param>
/// <param name="Type">
/// The message type as the owner sees it, with the transport's <c>ext/</c> prefix already removed.
/// </param>
/// <param name="Payload">
/// The message body as it arrived, which is JSON text rather than a parsed object. The owner knows
/// the shape it expects and the host does not, so it is handed over unread.
/// </param>
/// <param name="Buffers">
/// Binary payloads carried alongside the body, already decoded from their on-the-wire form.
/// </param>
public sealed record OutputChannelMessage(
    string ChannelId,
    string Type,
    string? Payload,
    IReadOnlyList<byte[]>? Buffers = null);
