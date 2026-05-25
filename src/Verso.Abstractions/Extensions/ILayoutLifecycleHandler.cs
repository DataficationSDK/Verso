namespace Verso.Abstractions;

/// <summary>
/// Capability interface for extensions that need per-mount lifecycle callbacks
/// for a layout owned by the same extension. Implement alongside an
/// <see cref="ILayoutEngine"/> on the same class (the common case) or as a
/// sibling class that shares the layout's <see cref="LayoutId"/>.
/// Routing is by the <c>(ExtensionId, LayoutId)</c> identity pair.
/// </summary>
/// <remarks>
/// Primarily relevant to <see cref="LayoutRendererIsolation.Isolated"/> layouts,
/// which own client-side state the host cannot see and therefore need callbacks
/// for "frame is alive" and "frame is going away." Inline layouts may implement
/// the interface to receive per-mount notifications or to push out-of-band
/// updates without waiting for an interaction. Layouts that do not implement
/// this interface incur no overhead.
/// </remarks>
public interface ILayoutLifecycleHandler : IExtension
{
    /// <summary>
    /// The <see cref="ILayoutEngine.LayoutId"/> this handler services. The
    /// handler's <see cref="IExtension.ExtensionId"/> supplies the other half
    /// of the identity pair. The handler's owning extension must also register
    /// an <see cref="ILayoutEngine"/> with this same <c>LayoutId</c>.
    /// </summary>
    string LayoutId { get; }

    /// <summary>
    /// Invoked after a renderer instance has signaled readiness and before the
    /// host completes its initialization handshake. The returned dictionary is
    /// merged into the host's init payload, giving the renderer authoritative
    /// initial state. Use the supplied <see cref="LayoutRendererMountContext.Frame"/>
    /// to push later messages while the renderer is alive.
    /// </summary>
    Task<IDictionary<string, object>?> OnRendererMountedAsync(
        LayoutRendererMountContext context);

    /// <summary>
    /// Invoked when a renderer instance is being torn down (notebook close,
    /// layout switch, kernel restart, manual remount). Cancel subscriptions
    /// and release per-frame resources. The frame channel obtained from the
    /// matching mount context is already marked dead before this method is
    /// invoked and becomes invalid after the method returns.
    /// </summary>
    Task OnRendererUnmountedAsync(LayoutRendererUnmountContext context);
}
