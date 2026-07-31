namespace Verso.Abstractions;

/// <summary>
/// Capability interface for extensions that handle user actions raised from a
/// panel owned by the same extension. Implement alongside an
/// <see cref="INotebookPanel"/> on the same class (the common case) or as a
/// sibling class that shares the panel's <see cref="PanelId"/>. Routing is by
/// the <c>(ExtensionId, PanelId)</c> identity pair.
/// </summary>
public interface IPanelInteractionHandler : IExtension
{
    /// <summary>
    /// The <see cref="INotebookPanel.PanelId"/> this handler services. The
    /// handler's <see cref="IExtension.ExtensionId"/> supplies the other half of
    /// the identity pair. The handler's owning extension must also register an
    /// <see cref="INotebookPanel"/> with this same <c>PanelId</c>.
    /// </summary>
    string PanelId { get; }

    /// <summary>
    /// Invoked when the host reports that the user triggered an action in the
    /// panel. Implementations should update their own state and call
    /// <see cref="PanelInteractionContext.RequestRefresh"/> when the panel's
    /// content has changed as a result.
    /// </summary>
    /// <remarks>
    /// How a host detects an action is the host's business and varies by host.
    /// Interaction types in the <c>verso/</c> namespace are reserved by the host
    /// and are intercepted before this handler is invoked, so using that prefix
    /// as an application-defined type will silently never reach the handler.
    /// </remarks>
    /// <param name="context">Describes the action that was triggered.</param>
    Task OnPanelInteractionAsync(PanelInteractionContext context);
}
