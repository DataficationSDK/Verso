using Verso.Abstractions;

namespace Verso.Host.Layouts;

/// <summary>
/// Abstract host-side base for <see cref="ILayoutFrameChannel"/> implementations.
/// Owns the <c>verso/</c> reserved-namespace guard and the lifetime flag so concrete
/// channels only need to implement the underlying message delivery primitive.
/// </summary>
internal abstract class LayoutFrameChannel : ILayoutFrameChannel
{
    private volatile bool _isAlive = true;

    protected LayoutFrameChannel(string frameInstanceId)
    {
        FrameInstanceId = frameInstanceId;
    }

    public string FrameInstanceId { get; }

    public bool IsAlive => _isAlive;

    public Task PostMessageAsync(string type, object? payload, CancellationToken ct = default)
    {
        EnsureValidMessageType(type);

        if (!_isAlive)
        {
            throw new InvalidOperationException(
                $"Cannot post message of type '{type}': the frame channel for " +
                $"'{FrameInstanceId}' has been marked dead.");
        }

        return PostMessageCoreAsync(type, payload, ct);
    }

    /// <summary>
    /// Marks the channel as no longer attached to a live renderer. Subsequent
    /// <see cref="PostMessageAsync"/> calls fail with <see cref="InvalidOperationException"/>.
    /// Invoked by the host before the matching <see cref="ILayoutLifecycleHandler.OnRendererUnmountedAsync"/>
    /// so handler code observes <see cref="IsAlive"/> as <c>false</c>.
    /// </summary>
    internal void MarkDead() => _isAlive = false;

    /// <summary>
    /// Delivers an already-validated, <c>ext/</c>-prefixed message to the renderer.
    /// Concrete subclasses implement the transport (postMessage to an iframe,
    /// JS interop call, captured list for tests, etc.).
    /// </summary>
    protected abstract Task PostMessageCoreAsync(string type, object? payload, CancellationToken ct);

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="type"/> is null,
    /// empty, or starts with the reserved <c>verso/</c> prefix. Shared with test
    /// fakes that implement <see cref="ILayoutFrameChannel"/> directly.
    /// </summary>
    internal static void EnsureValidMessageType(string type)
    {
        if (string.IsNullOrEmpty(type))
            throw new ArgumentException("Message type must be a non-empty string.", nameof(type));

        if (type.StartsWith("verso/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Message type '{type}' is in the reserved 'verso/' namespace. " +
                $"Extensions must use the 'ext/'-prefixed namespace (the host applies the prefix automatically).",
                nameof(type));
        }
    }
}
