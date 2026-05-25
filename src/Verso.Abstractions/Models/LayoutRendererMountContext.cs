namespace Verso.Abstractions;

/// <summary>
/// Context provided to <see cref="ILayoutLifecycleHandler.OnRendererMountedAsync"/>
/// describing a freshly mounted renderer instance.
/// </summary>
public sealed class LayoutRendererMountContext
{
    /// <summary>
    /// The owning extension's <see cref="IExtension.ExtensionId"/>. Together with
    /// <see cref="LayoutId"/> this forms the identity pair the host routed against.
    /// </summary>
    public string ExtensionId { get; init; } = "";

    /// <summary>
    /// The <see cref="ILayoutEngine.LayoutId"/> the renderer instance belongs to.
    /// </summary>
    public string LayoutId { get; init; } = "";

    /// <summary>
    /// Host-allocated, opaque token identifying this renderer instance. Stable
    /// for the life of the mount and matches the
    /// <see cref="LayoutRendererUnmountContext.FrameInstanceId"/> delivered on
    /// teardown.
    /// </summary>
    public string FrameInstanceId { get; init; } = "";

    /// <summary>
    /// Isolation boundary the renderer was instantiated under.
    /// </summary>
    public LayoutRendererIsolation Isolation { get; init; }

    /// <summary>
    /// Verso context providing access to variables, theme, notebook operations,
    /// and the extension host.
    /// </summary>
    public IVersoContext Verso { get; init; } = null!;

    /// <summary>
    /// Push channel for sending <c>ext/*</c> messages to the renderer while it
    /// is alive. The channel becomes invalid after the matching
    /// <see cref="ILayoutLifecycleHandler.OnRendererUnmountedAsync"/> returns.
    /// </summary>
    public ILayoutFrameChannel Frame { get; init; } = null!;

    /// <summary>
    /// Cancellation token for the mount operation.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
