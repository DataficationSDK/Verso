// Layout static-asset interop. Backs INotebookService.GetLayoutStaticAssetsAsync on the
// WASM host: materializes base64-encoded asset bytes into Blob objects, mints stable
// blob: URLs keyed by (extensionId, layoutId, assetId), and revokes them on layout
// switch so the browser releases the underlying bytes.
//
// The matching server host serves assets from a /_verso/layout-assets/... endpoint
// instead; both hosts surface the same LayoutStaticAssetDescriptor shape to the Razor
// component layer.

(function () {
    // Map of `${extensionId}|${layoutId}|${assetId}` -> blob URL string.
    var urlByKey = new Map();

    function key(extensionId, layoutId, assetId) {
        return extensionId + '|' + layoutId + '|' + assetId;
    }

    function base64ToBytes(base64) {
        var binary = atob(base64);
        var len = binary.length;
        var bytes = new Uint8Array(len);
        for (var i = 0; i < len; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    }

    function registerAsset(extensionId, layoutId, assetId, contentType, base64) {
        var k = key(extensionId, layoutId, assetId);
        var existing = urlByKey.get(k);
        if (existing) {
            return existing;
        }

        var bytes = base64ToBytes(base64);
        var blob = new Blob([bytes], { type: contentType || 'application/octet-stream' });
        var url = URL.createObjectURL(blob);
        urlByKey.set(k, url);
        return url;
    }

    function releaseLayout(extensionId, layoutId) {
        var prefix = extensionId + '|' + layoutId + '|';
        var toRemove = [];
        urlByKey.forEach(function (url, k) {
            if (k.indexOf(prefix) === 0) {
                URL.revokeObjectURL(url);
                toRemove.push(k);
            }
        });
        for (var i = 0; i < toRemove.length; i++) {
            urlByKey.delete(toRemove[i]);
        }
    }

    function releaseAll() {
        urlByKey.forEach(function (url) {
            URL.revokeObjectURL(url);
        });
        urlByKey.clear();
    }

    window.versoLayoutAssets = {
        registerAsset: registerAsset,
        releaseLayout: releaseLayout,
        releaseAll: releaseAll
    };
})();
