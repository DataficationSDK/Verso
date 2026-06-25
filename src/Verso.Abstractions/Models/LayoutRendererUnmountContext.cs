namespace Verso.Abstractions;

/// <summary>
/// Context provided to <see cref="ILayoutLifecycleHandler.OnRendererUnmountedAsync"/>
/// describing a renderer instance that is being torn down.
/// </summary>
public sealed class LayoutRendererUnmountContext
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
    /// Host-allocated token identifying the renderer instance being unmounted.
    /// Matches the <see cref="LayoutRendererMountContext.FrameInstanceId"/>
    /// delivered when the instance was mounted.
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
    /// Cancellation token for the unmount operation.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
