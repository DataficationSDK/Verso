namespace Verso.Abstractions;

/// <summary>
/// Server-to-renderer push channel held by an <see cref="ILayoutLifecycleHandler"/>
/// for the life of one renderer instance. The host realizes the channel using
/// whatever mechanism is appropriate for the isolation boundary; extensions see
/// only this contract.
/// </summary>
public interface ILayoutFrameChannel
{
    /// <summary>
    /// Host-allocated, opaque token identifying the renderer instance this
    /// channel writes to. Stable for the life of one mount.
    /// </summary>
    string FrameInstanceId { get; }

    /// <summary>
    /// Whether the channel is still attached to a live renderer. Flips to
    /// <c>false</c> before <see cref="ILayoutLifecycleHandler.OnRendererUnmountedAsync"/>
    /// is invoked. Once it is <c>false</c>, <see cref="PostMessageAsync"/> throws
    /// <see cref="InvalidOperationException"/> rather than dropping the message, so a
    /// caller that posts from a callback able to outlive the renderer (for example a
    /// variable-changed handler that can race the unmount) should check this property
    /// first.
    /// </summary>
    bool IsAlive { get; }

    /// <summary>
    /// Posts a typed message to the renderer. The host prefixes the supplied
    /// <paramref name="type"/> with <c>"ext/"</c> before delivery.
    /// </summary>
    /// <remarks>
    /// A dead channel or an invalid <paramref name="type"/> is never silently dropped:
    /// both throw synchronously, before the returned task is awaited. Guard with
    /// <see cref="IsAlive"/> when posting from a callback that can run after the renderer
    /// has unmounted.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="type"/> is null or empty, or is in the reserved <c>verso/</c>
    /// namespace, which is held for framework messages (the host adds the <c>ext/</c>
    /// prefix automatically, so extensions never supply it).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The channel is no longer attached to a live renderer
    /// (<see cref="IsAlive"/> is <c>false</c>).
    /// </exception>
    Task PostMessageAsync(string type, object? payload, CancellationToken ct = default);
}
