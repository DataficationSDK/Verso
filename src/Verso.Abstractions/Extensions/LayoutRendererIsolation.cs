namespace Verso.Abstractions;

/// <summary>
/// Describes how a layout's renderer relates to the host's UI execution context.
/// Host-neutral: the enum names the relationship, not any specific UI technology.
/// </summary>
public enum LayoutRendererIsolation
{
    /// <summary>
    /// Renderer shares the host's UI execution context. The layout supplies content
    /// through <see cref="ILayoutEngine.RenderLayoutAsync"/> in a format the host
    /// accepts and relies on host-provided event routing for interactions.
    /// </summary>
    Inline = 0,

    /// <summary>
    /// Renderer runs inside a host-provided isolation boundary with its own
    /// execution context. The layout supplies a renderer module through
    /// <see cref="ILayoutEngine.GetRendererPackageAsync"/>; the host realizes the
    /// boundary using whatever mechanism is appropriate for the host family and
    /// bridges interactions through a documented message contract.
    /// </summary>
    Isolated = 1
}

/// <summary>
/// Wire-format helpers for <see cref="LayoutRendererIsolation"/>.
/// </summary>
public static class LayoutRendererIsolationExtensions
{
    /// <summary>
    /// Returns the lowercase string form used in JSON-RPC payloads
    /// (<c>"inline"</c> or <c>"isolated"</c>).
    /// </summary>
    public static string ToWireString(this LayoutRendererIsolation isolation) =>
        isolation switch
        {
            LayoutRendererIsolation.Isolated => "isolated",
            _ => "inline"
        };
}
