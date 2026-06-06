using Verso;
using Verso.Abstractions;
using Verso.Extensions;

namespace Verso.Blazor.Services;

/// <summary>
/// App-singleton fallback that regenerates layout static assets on demand when the
/// per-circuit <see cref="LayoutAssetCache"/> has no entry. The cache is populated
/// lazily, only while a notebook using the layout is mounted on a live circuit, so a
/// fresh process, a server restart, or a prerender that emits an asset URL before the
/// circuit registers would otherwise leave the <c>/_verso/layout-assets</c> endpoint
/// serving 404. This provider mirrors the out-of-process host, which generates assets
/// statelessly on every request, so the URL stays valid regardless of circuit state.
///
/// It resolves layouts the same way every notebook does (built-in extensions plus the
/// configured extensions directory), keyed by their <c>(ExtensionId, LayoutId)</c> pair,
/// so there is no first-party special-casing. Notebook-scoped layouts loaded by a single
/// document are not known here and remain served from the per-circuit cache.
/// </summary>
public sealed class LayoutAssetProvider
{
    // Must match the capability set ServerNotebookService advertises so on-demand
    // generation produces the same assets the circuit path would register.
    private static readonly LayoutHostCapabilities HtmlHostCapabilities = new(
        new HashSet<string>(StringComparer.Ordinal)
        {
            "text/css",
            "text/javascript",
            "application/javascript",
        },
        new HashSet<string>(StringComparer.Ordinal) { "text/html" });

    private readonly NotebookServiceOptions _options;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private ExtensionHost? _host;
    private BlazorLayoutRenderContext? _context;

    public LayoutAssetProvider(NotebookServiceOptions? options = null)
        => _options = options ?? new NotebookServiceOptions();

    /// <summary>
    /// Generates the bytes for a single layout asset, or <c>null</c> when no enabled
    /// layout with that identity advertises the asset. The render context is a throwaway
    /// empty notebook: layout asset generation is host-level (a scoped stylesheet or
    /// script), not tied to any document's contents.
    /// </summary>
    public async Task<(string ContentType, byte[] Content)?> TryGenerateAsync(
        string extensionId, string layoutId, string assetId)
    {
        var host = await EnsureHostAsync();
        if (host is null || _context is null)
            return null;

        if (!host.TryGetLayoutEngine(extensionId, layoutId, out var engine))
            return null;

        var assets = await engine.GetStaticAssetsAsync(_context, HtmlHostCapabilities);
        if (assets is null)
            return null;

        foreach (var asset in assets)
        {
            if (string.Equals(asset.AssetId, assetId, StringComparison.Ordinal)
                && HtmlHostCapabilities.SupportedAssetContentTypes.Contains(asset.ContentType))
                return (asset.ContentType, asset.Content);
        }

        return null;
    }

    private async Task<ExtensionHost?> EnsureHostAsync()
    {
        if (_host is not null)
            return _host;

        await _initGate.WaitAsync();
        try
        {
            if (_host is not null)
                return _host;

            var host = new ExtensionHost();
            await host.LoadBuiltInExtensionsAsync();

            foreach (var dir in _options.GetAllExtensionsDirectories())
            {
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    await host.LoadFromDirectoryAsync(dir);
            }

            _context = new BlazorLayoutRenderContext(new Scaffold(new NotebookModel(), host));
            _host = host;
            return _host;
        }
        finally
        {
            _initGate.Release();
        }
    }
}
