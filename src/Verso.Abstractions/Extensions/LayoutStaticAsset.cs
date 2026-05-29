namespace Verso.Abstractions;

/// <summary>
/// A single presentation asset declared by an <see cref="ILayoutEngine"/> for the host to
/// load alongside the layout. Returned from
/// <see cref="ILayoutEngine.GetStaticAssetsAsync"/> and consumed only by hosts that
/// advertise the asset's content type in their <see cref="LayoutHostCapabilities"/>.
/// </summary>
/// <param name="AssetId">
/// Stable, layout-relative key (e.g. <c>"dashboard.css"</c>). The host uses this to cache
/// the asset across re-renders and to construct per-asset DOM identifiers. To force a
/// refresh after the bytes change, include a version suffix in the id
/// (e.g. <c>"dashboard.css?v=2"</c>); the bare id alone is not content-hashed.
/// </param>
/// <param name="ContentType">
/// IANA media type of the asset (e.g. <c>"text/css"</c>). The host only loads assets whose
/// content type was advertised in the capabilities exchange.
/// </param>
/// <param name="Content">
/// The asset bytes. The host is free to serve them via a host-appropriate transport
/// (custom endpoint, blob URL, inline) and may cache the bytes for the lifetime of the
/// owning extension.
/// </param>
public sealed record LayoutStaticAsset(
    string AssetId,
    string ContentType,
    byte[] Content);
