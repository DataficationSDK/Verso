namespace Verso.Abstractions;

/// <summary>
/// Describes what a host can consume from an <see cref="ILayoutEngine"/> at the
/// presentation layer. Passed to
/// <see cref="ILayoutEngine.GetStaticAssetsAsync"/> so the engine can return only the
/// assets the host knows how to load, and reserved for a future render-format
/// negotiation on <see cref="ILayoutEngine.RenderLayoutAsync"/>.
/// </summary>
/// <param name="SupportedAssetContentTypes">
/// IANA media types the host can load as layout-scoped static assets
/// (e.g. <c>"text/css"</c> for HTML hosts). An engine that returns an asset with a
/// content type not in this set should expect the host to ignore it.
/// </param>
/// <param name="SupportedRenderFormats">
/// IANA media types the host can render from <see cref="ILayoutEngine.RenderLayoutAsync"/>.
/// HTML hosts advertise <c>"text/html"</c>. Reserved for a future render-format
/// negotiation; the host advertises the set so engines can branch on the active host
/// surface today, even before the negotiation arrives.
/// </param>
public sealed record LayoutHostCapabilities(
    IReadOnlySet<string> SupportedAssetContentTypes,
    IReadOnlySet<string> SupportedRenderFormats);
