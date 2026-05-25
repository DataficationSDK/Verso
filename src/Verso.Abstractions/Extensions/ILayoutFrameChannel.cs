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
    /// is invoked; calls to <see cref="PostMessageAsync"/> after that point fail.
    /// </summary>
    bool IsAlive { get; }

    /// <summary>
    /// Posts a typed message to the renderer. The host prefixes the supplied
    /// <paramref name="type"/> with <c>"ext/"</c> before delivery; types in
    /// the reserved <c>verso/</c> namespace are rejected with
    /// <see cref="ArgumentException"/> to prevent collision with framework messages.
    /// </summary>
    Task PostMessageAsync(string type, object? payload, CancellationToken ct = default);
}
