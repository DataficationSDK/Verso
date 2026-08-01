namespace Verso.Abstractions;

/// <summary>
/// Contributes a panel to the notebook UI, shown alongside the notebook body and
/// toggled from the host's panel controls. Implement this to surface information
/// or actions that belong next to the notebook rather than inside a cell.
/// </summary>
/// <remarks>
/// <para>
/// A panel's identity is the <c>(ExtensionId, PanelId)</c> pair, so two extensions
/// may use the same <see cref="PanelId"/> without colliding. Registering two panels
/// with the same pair is an error.
/// </para>
/// <para>
/// Panels describe their content rather than draw it. <see cref="RenderAsync"/>
/// returns representations of the same content in preference order, and each host
/// renders the first one it understands. No host is obliged to support any
/// particular media type, so a panel that offers only <c>text/html</c> is simply
/// unavailable on a host that does not render HTML. Offering a plain-text
/// alternative is the cheapest way to stay useful everywhere.
/// </para>
/// <para>
/// To respond to user actions in the panel, implement
/// <see cref="IPanelInteractionHandler"/> on the same class or on a sibling class
/// that shares this <see cref="PanelId"/>.
/// </para>
/// </remarks>
public interface INotebookPanel : IExtension
{
    /// <summary>
    /// Identifier for this panel, unique within the owning extension
    /// (e.g. "findings", "profiler").
    /// </summary>
    string PanelId { get; }

    /// <summary>
    /// Human-readable name shown as the panel's title and as the accessible name
    /// of its toggle control.
    /// </summary>
    /// <remarks>
    /// A toggle shows an icon at rest, so the name alone may be the first and only
    /// thing a reader gets. Hosts show <see cref="IExtension.Description"/> beneath
    /// it in the toggle's tooltip, which is where to say what the panel contains.
    /// </remarks>
    string DisplayName { get; }

    /// <summary>
    /// Name of an icon from the host's icon set, which the host maps to whatever
    /// glyph it draws with. Returning <c>null</c>, or a name the host does not
    /// recognize, falls back to <see cref="IconMarkup"/> and then to
    /// <see cref="DisplayName"/>.
    /// </summary>
    /// <remarks>
    /// The names hosts are expected to recognize are: <c>document</c>, <c>list</c>,
    /// <c>puzzle</c>, <c>braces</c>, <c>gear</c>, <c>search</c>, <c>layout</c>,
    /// <c>compare</c>, <c>info</c>, <c>warning</c>, <c>flag</c>, <c>check</c>,
    /// <c>clock</c>, <c>tag</c>, <c>chart</c>, <c>table</c>, <c>folder</c>, and
    /// <c>link</c>. Hosts that cannot draw a given name fall back rather than fail.
    /// </remarks>
    string? IconName { get; }

    /// <summary>
    /// Optional host-specific icon markup used in place of <see cref="IconName"/>
    /// by hosts that understand it. An escape hatch for panels that need a custom
    /// icon and accept that other hosts will fall back. Defaults to <c>null</c>.
    /// </summary>
    string? IconMarkup => null;

    /// <summary>
    /// Sort order among the host's panel controls. Lower values appear first.
    /// The built-in panels occupy 100 through 500 in steps of 100, so a value
    /// between them places a panel among them and a larger value places it after.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Determines whether the panel should be offered at all in the current state.
    /// A panel that returns <c>false</c> is omitted from the host's panel controls,
    /// and is closed if it was open. Called when the host builds its panel list,
    /// which happens on notebook load and whenever the layout or the set of loaded
    /// extensions changes, so implementations should be cheap.
    /// </summary>
    /// <remarks>
    /// Availability is not re-evaluated on every cell selection. A panel whose
    /// content depends on the selection should stay available and say so in
    /// <see cref="RenderAsync"/>, which the host does call on selection change.
    /// </remarks>
    /// <param name="context">Context providing notebook state and Verso services.</param>
    /// <returns><c>true</c> if the panel should be available; otherwise <c>false</c>.</returns>
    Task<bool> IsAvailableAsync(IPanelContext context);

    /// <summary>
    /// Produces the panel's content as one or more representations, ordered from
    /// richest to simplest. The host renders the first entry whose
    /// <see cref="RenderResult.MimeType"/> it supports and ignores the rest.
    /// Returning an empty list renders the panel as empty.
    /// </summary>
    /// <param name="context">Context providing notebook state and Verso services.</param>
    /// <returns>Representations of the panel's content in preference order.</returns>
    Task<IReadOnlyList<RenderResult>> RenderAsync(IPanelContext context);
}
