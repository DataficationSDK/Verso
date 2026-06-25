using Verso.Host.Protocol;

namespace Verso.Host.Layouts;

/// <summary>
/// Concrete <see cref="LayoutFrameChannel"/> that delivers extension-pushed messages
/// to a browser-hosted iframe by emitting a <c>layout/frameMessage</c> notification
/// on the owning session. The notification carries the destination frame instance id,
/// the on-wire message type (always prefixed with <c>ext/</c>), and the extension's
/// payload. The client demultiplexes by frame instance id and forwards the envelope
/// into the matching iframe via <c>postMessage</c>.
/// </summary>
internal sealed class IframeFrameChannel : LayoutFrameChannel
{
    private readonly NotebookSession _session;

    public IframeFrameChannel(string frameInstanceId, NotebookSession session)
        : base(frameInstanceId)
    {
        _session = session;
    }

    protected override Task PostMessageCoreAsync(string type, object? payload, CancellationToken ct)
    {
        _session.SendNotification(MethodNames.LayoutFrameMessage, new
        {
            frameInstanceId = FrameInstanceId,
            type = "ext/" + type,
            payload
        });
        return Task.CompletedTask;
    }
}
