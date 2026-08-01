namespace Verso.Abstractions;

/// <summary>
/// Context provided to <see cref="IPanelInteractionHandler.OnPanelInteractionAsync"/>
/// describing a panel-scoped action triggered by the user.
/// </summary>
/// <remarks>
/// Nothing here names a rendering technology. The host decides how a user triggers
/// an action and reports the result in these terms, so the same handler serves any
/// host that can present the panel's content.
/// </remarks>
public sealed class PanelInteractionContext
{
    /// <summary>
    /// The owning extension's <see cref="IExtension.ExtensionId"/>. Together with
    /// <see cref="PanelId"/> this forms the identity pair that scoped the action.
    /// Present for symmetry with the host surface; a handler implemented as a
    /// sibling class shares this id with its panel.
    /// </summary>
    public string ExtensionId { get; init; } = "";

    /// <summary>
    /// The <see cref="INotebookPanel.PanelId"/> the action targets.
    /// </summary>
    public string PanelId { get; init; } = "";

    /// <summary>
    /// Application-defined action name (e.g. <c>"accept"</c>, <c>"refresh"</c>).
    /// </summary>
    public string InteractionType { get; init; } = "";

    /// <summary>
    /// Free-form payload from the host (e.g. JSON, form data).
    /// </summary>
    public string Payload { get; init; } = "";

    /// <summary>
    /// Optional identifier supplied by the host to disambiguate which item within
    /// the panel the action applies to.
    /// </summary>
    public string? TargetId { get; init; }

    /// <summary>
    /// Identifier of the cell selected when the action was triggered, or
    /// <c>null</c> if none was selected.
    /// </summary>
    public Guid? SelectedCellId { get; init; }

    /// <summary>
    /// Verso context providing access to variables, theme, notebook operations,
    /// and the extension host. The host populates this when invoking
    /// <see cref="IPanelInteractionHandler.OnPanelInteractionAsync"/>.
    /// </summary>
    public IVersoContext Verso { get; init; } = null!;

    /// <summary>
    /// Cancellation token for the interaction operation.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Requests that the host discard the panel's current content and ask the
    /// panel to produce it again. Call this after an action changes what the panel
    /// should show. The default is a no-op; the host installs a real receiver
    /// before dispatching the context.
    /// </summary>
    public Action RequestRefresh { get; init; } = static () => { };
}
