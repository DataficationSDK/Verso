namespace Verso.Abstractions;

/// <summary>
/// Context provided to <see cref="ILayoutInteractionHandler.OnLayoutInteractionAsync"/>
/// describing a layout-scoped interaction originated by the client.
/// </summary>
public sealed class LayoutInteractionContext
{
    /// <summary>
    /// The owning extension's <see cref="IExtension.ExtensionId"/>. Together with
    /// <see cref="LayoutId"/> this forms the identity pair that scoped the
    /// interaction. Present for symmetry with the JSON-RPC surface; a handler
    /// implemented as a sibling class shares this id with its layout engine.
    /// </summary>
    public string ExtensionId { get; init; } = "";

    /// <summary>
    /// The <see cref="ILayoutEngine.LayoutId"/> the interaction targets.
    /// </summary>
    public string LayoutId { get; init; } = "";

    /// <summary>
    /// Host-allocated, opaque token identifying which renderer instance
    /// originated the interaction. Stable for the life of one mount.
    /// Extensions should treat the value as a string token, not parse it.
    /// </summary>
    public string FrameInstanceId { get; init; } = "";

    /// <summary>
    /// Application-defined interaction type (e.g. <c>"setEditMode"</c>,
    /// <c>"updateCellPosition"</c>).
    /// </summary>
    public string InteractionType { get; init; } = "";

    /// <summary>
    /// Free-form payload from the client (e.g. JSON, form data).
    /// </summary>
    public string Payload { get; init; } = "";

    /// <summary>
    /// Optional identifier supplied by the client to disambiguate the target
    /// within a layout (e.g. a clicked element's <c>data-target-id</c>).
    /// </summary>
    public string? TargetId { get; init; }

    /// <summary>
    /// Verso context providing access to variables, theme, notebook operations,
    /// and the extension host. The host populates this when invoking
    /// <see cref="ILayoutInteractionHandler.OnLayoutInteractionAsync"/>.
    /// </summary>
    public IVersoContext Verso { get; init; } = null!;

    /// <summary>
    /// Cancellation token for the interaction operation.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Requests that the client re-fetch the full layout HTML via
    /// <c>layout/render</c>. Equivalent to firing a <c>layout/updated</c>
    /// notification with <c>scope="full"</c>. The default is a no-op; the host
    /// installs a real receiver before dispatching the context.
    /// </summary>
    public Action RequestRender { get; init; } = static () => { };

    /// <summary>
    /// Requests that the client invalidate cached container info for the
    /// specified cell and re-fetch <c>layout/getCellContainer</c> for it.
    /// The default is a no-op; the host installs a real receiver before
    /// dispatching the context.
    /// </summary>
    public Action<Guid> RequestCellRefresh { get; init; } = static _ => { };
}
