using System.Collections.Concurrent;

namespace Verso.Blazor.Services;

/// <summary>
/// Server-side cache backing the <c>/_verso/layout-assets</c> endpoint. Layout-scoped
/// static assets are registered here when a layout is activated; the endpoint streams
/// the bytes on demand so the host can serve a stable URL via <c>&lt;link&gt;</c>.
/// Keyed by <c>(ExtensionId, LayoutId, AssetId)</c> with ordinal comparison; assets are
/// not content-hashed by this cache, so layout authors version-bust by including a
/// suffix in <c>AssetId</c>.
/// </summary>
public sealed class LayoutAssetCache
{
    private readonly ConcurrentDictionary<(string ExtensionId, string LayoutId, string AssetId), Entry> _entries
        = new();

    public void Register(string extensionId, string layoutId, string assetId,
        string contentType, byte[] content)
    {
        _entries[(extensionId, layoutId, assetId)] = new Entry(contentType, content);
    }

    public bool TryGet(string extensionId, string layoutId, string assetId,
        out string contentType, out byte[] content)
    {
        if (_entries.TryGetValue((extensionId, layoutId, assetId), out var entry))
        {
            contentType = entry.ContentType;
            content = entry.Content;
            return true;
        }
        contentType = "";
        content = Array.Empty<byte>();
        return false;
    }

    /// <summary>
    /// Drops every entry scoped to the given <c>(extensionId, layoutId)</c> pair, e.g.
    /// when the owning extension is reloaded so a stale URL no longer resolves to old
    /// bytes after the layout is re-mounted.
    /// </summary>
    public void ReleaseLayout(string extensionId, string layoutId)
    {
        foreach (var key in _entries.Keys.ToList())
        {
            if (string.Equals(key.ExtensionId, extensionId, StringComparison.Ordinal)
                && string.Equals(key.LayoutId, layoutId, StringComparison.Ordinal))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    public static string BuildUrl(string extensionId, string layoutId, string assetId)
    {
        return "/_verso/layout-assets"
            + "?ext=" + Uri.EscapeDataString(extensionId)
            + "&layout=" + Uri.EscapeDataString(layoutId)
            + "&asset=" + Uri.EscapeDataString(assetId);
    }

    private readonly record struct Entry(string ContentType, byte[] Content);
}
