using System.Text.Json;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

/// <summary>
/// The out-of-process host's inbound half of an output channel: what a view says on its way to
/// whoever owns the channel it is drawing. Both methods hand straight to the session's channel
/// host, which is where the channel lookup, the version check, and the delivery to owners live, so
/// this and the served host's equivalent stay two spellings of the same thing rather than two
/// behaviours.
/// </summary>
public static class OutputChannelHandler
{
    /// <summary>
    /// A view reporting that it has mounted, which releases whatever its channel held for it. A
    /// report naming a channel that is not open does nothing, which is the ordinary case for a view
    /// that outlived the cell that drew it.
    /// </summary>
    public static async Task<object?> HandleReadyAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ChannelReadyParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for channel/ready");

        if (string.IsNullOrWhiteSpace(p.ChannelId))
            throw new JsonException("channel/ready requires 'channelId'.");

        await ns.Scaffold.OutputChannels.HandleReadyAsync(p.ChannelId, p.ProtocolVersion);

        // Nothing to say back. The view is not waiting on an answer, and a result would only be
        // something else to keep in step.
        return null;
    }

    /// <summary>
    /// A message from a view. Buffers arrive base64-encoded and are decoded here, so an owner is
    /// handed bytes wherever its message came from.
    /// </summary>
    public static async Task<object?> HandleMessageAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<ChannelMessageParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for channel/message");

        if (string.IsNullOrWhiteSpace(p.ChannelId))
            throw new JsonException("channel/message requires 'channelId'.");
        if (string.IsNullOrWhiteSpace(p.MessageType))
            throw new JsonException("channel/message requires 'messageType'.");

        await ns.Scaffold.OutputChannels.HandleMessageAsync(
            p.ChannelId, p.MessageType, p.Payload, DecodeBuffers(p.Buffers));

        return null;
    }

    /// <summary>
    /// Decodes the base64 buffers of one message, or returns null when there are none. A buffer
    /// that will not decode is dropped rather than failing the message: the rest of it is still
    /// worth delivering, and an owner reading a buffer it did not get fails where it can say so.
    /// </summary>
    private static IReadOnlyList<byte[]>? DecodeBuffers(IReadOnlyList<string>? buffers)
    {
        if (buffers is null || buffers.Count == 0) return null;

        var decoded = new List<byte[]>(buffers.Count);
        foreach (var buffer in buffers)
        {
            try { decoded.Add(Convert.FromBase64String(buffer)); }
            catch (FormatException) { }
        }

        return decoded.Count == 0 ? null : decoded;
    }
}
