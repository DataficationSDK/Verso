namespace Verso.Abstractions;

/// <summary>
/// Capability interface for extensions that handle bidirectional interactions
/// scoped to a layout owned by the same extension. Implement alongside an
/// <see cref="ILayoutEngine"/> on the same class (the common case) or as a
/// sibling class that shares the layout's <see cref="LayoutId"/>.
/// Routing is by the <c>(ExtensionId, LayoutId)</c> identity pair.
/// </summary>
public interface ILayoutInteractionHandler : IExtension
{
    /// <summary>
    /// The <see cref="ILayoutEngine.LayoutId"/> this handler services. The
    /// handler's <see cref="IExtension.ExtensionId"/> supplies the other half
    /// of the identity pair. The handler's owning extension must also register
    /// an <see cref="ILayoutEngine"/> with this same <c>LayoutId</c>.
    /// </summary>
    string LayoutId { get; }

    /// <summary>
    /// Invoked when the client routes a layout interaction (via the
    /// <c>layout/interact</c> JSON-RPC method) to this handler. Implementations
    /// should mutate layout state, persist it through
    /// <see cref="LayoutInteractionContext.Verso"/> when applicable, and call
    /// <see cref="LayoutInteractionContext.RequestRender"/> or
    /// <see cref="LayoutInteractionContext.RequestCellRefresh"/> to drive UI
    /// updates. Returns <see cref="Task"/> only; layout interactions do not
    /// return payloads to the client.
    /// </summary>
    Task OnLayoutInteractionAsync(LayoutInteractionContext context);
}
