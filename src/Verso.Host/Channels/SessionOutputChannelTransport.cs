using Verso.Execution;
using Verso.Host.Protocol;

namespace Verso.Host.Channels;

/// <summary>
/// Carries a channel's traffic to the view drawing it by writing a notification on the owning
/// session. The client demultiplexes by channel id and hands the message to the same interop the
/// served host calls directly, so what a document sees does not depend on which host it is running
/// under.
/// </summary>
/// <remarks>
/// The message type is passed through as it arrives, still carrying its <c>ext/</c> prefix. The
/// prefix comes off at the last hop, in the interop, which is the one place both hosts share and
/// therefore the one place it has to happen.
/// </remarks>
internal sealed class SessionOutputChannelTransport : IOutputChannelTransport
{
    private readonly NotebookSession _session;

    public SessionOutputChannelTransport(NotebookSession session) => _session = session;

    public Task PostAsync(string channelId, string messageType, object? payload, CancellationToken ct)
    {
        _session.SendNotification(MethodNames.ChannelPost, new
        {
            channelId,
            messageType,
            payload
        });
        return Task.CompletedTask;
    }

    public Task CloseAsync(string channelId, string reason, CancellationToken ct)
    {
        _session.SendNotification(MethodNames.ChannelClosed, new
        {
            channelId,
            reason
        });
        return Task.CompletedTask;
    }
}
