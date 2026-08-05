namespace Verso.Host.Dto;

// --- Output channels ---

/// <summary>
/// A view reporting that it has mounted and can receive on the channel it was given.
/// </summary>
public sealed class ChannelReadyParams
{
    public string ChannelId { get; set; } = "";

    /// <summary>
    /// The protocol version the view declares, absent when it declares none. Compared against the
    /// host's by the channel host, which decides what a mismatch means.
    /// </summary>
    public string? ProtocolVersion { get; set; }
}

/// <summary>
/// A message from a view, on its way to whoever owns its channel.
/// </summary>
public sealed class ChannelMessageParams
{
    public string ChannelId { get; set; } = "";
    public string MessageType { get; set; } = "";

    /// <summary>
    /// The message body as JSON text. It stays text the whole way: the owner is the only part that
    /// knows what shape it expects, and nothing in between gains anything by parsing it.
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>
    /// Binary parts, base64-encoded, absent when there are none. Base64 is what both this transport
    /// and the browser boundary carry as ordinary JSON.
    /// </summary>
    public List<string>? Buffers { get; set; }
}
