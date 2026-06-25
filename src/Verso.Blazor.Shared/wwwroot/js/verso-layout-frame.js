// Parent-side interop for the isolated (Tier 2) layout renderer. Mediates the
// bidirectional postMessage channel between the WASM page and a sandboxed iframe.
//
// Inbound (iframe -> host): a 'message' listener filters by event.source matching
// the registered iframe's contentWindow, validates the envelope shape ({type, payload}),
// and forwards to the Blazor component via DotNetObjectReference.invokeMethodAsync.
//
// Outbound (host -> iframe): postToIframe wraps iframe.contentWindow.postMessage
// with the same envelope shape.

(function () {
    var handles = new Map();
    var nextId = 1;

    // Resolve the sibling bridge script's URL from this script's own URL rather than
    // against document.baseURI. Hosts that point <base href> at a synthetic origin
    // (e.g. the VS Code webview, whose base is vscode-webview://<id>/ so Blazor's
    // NavigationManager works) would otherwise resolve a relative "_content/..." fetch
    // to a path the resource server rejects with 403. This script is always served from
    // the real, access-permitted resource location, so deriving the bridge URL from it
    // works in the browser, the server host, and the VS Code webview alike.
    function resolveBridgeUrl() {
        try {
            var self = document.currentScript && document.currentScript.src;
            if (self) {
                return self.replace(/verso-layout-frame\.js(\?.*)?$/, 'verso-layout-bridge.js$1');
            }
        } catch (_) { /* fall through to the relative path */ }
        return '_content/Verso.Blazor.Shared/js/verso-layout-bridge.js';
    }

    var bridgeUrl = resolveBridgeUrl();

    function attach(iframeElem, dotnetRef, frameInstanceId) {
        if (!iframeElem || !dotnetRef) return null;

        var handleId = nextId++;
        var listener = function (event) {
            if (event.source !== iframeElem.contentWindow) return;
            var data = event.data;
            if (!data || typeof data !== 'object' || typeof data.type !== 'string') return;

            var payloadJson = null;
            if (data.payload !== undefined && data.payload !== null) {
                try {
                    payloadJson = JSON.stringify(data.payload);
                } catch (_) {
                    payloadJson = null;
                }
            }

            dotnetRef.invokeMethodAsync('OnFrameMessage', data.type, payloadJson);
        };

        window.addEventListener('message', listener);
        handles.set(handleId, { listener: listener, dotnetRef: dotnetRef, frameInstanceId: frameInstanceId });
        return handleId;
    }

    // Reads the live theme from the host page's --verso-* custom properties and
    // shapes it into the bundle the iframe bridge expects. Only meaningful inside a
    // VS Code webview, where the host page maps --verso-* onto the active --vscode-*
    // theme (so these values track the editor theme, including on theme change).
    // Returns null on hosts that resolve the bundle server-side (e.g. Blazor Server),
    // leaving any host-supplied theme payload untouched.
    function readVsCodeThemeBundle() {
        var cs;
        try { cs = getComputedStyle(document.documentElement); } catch (_) { return null; }
        if (!cs || !cs.getPropertyValue('--vscode-editor-background').trim()) return null;

        function read(name, fallbackName) {
            var v = cs.getPropertyValue(name).trim();
            if (!v && fallbackName) v = cs.getPropertyValue(fallbackName).trim();
            return v;
        }

        var map = {
            'bg.default': read('--verso-bg-default'),
            'bg.elevated': read('--verso-bg-elevated', '--verso-bg-default'),
            'fg.default': read('--verso-fg-default'),
            'fg.muted': read('--verso-fg-muted'),
            'border.default': read('--verso-border-default'),
            'accent': read('--verso-accent'),
            'font.family.mono': read('--verso-font-family-mono', '--verso-ui-font-family'),
            'font.family.sans': read('--verso-font-family-sans', '--verso-ui-font-family'),
            'font.size.base': read('--verso-font-size-base', '--verso-ui-font-size')
        };

        var tokens = {};
        for (var k in map) { if (map[k]) tokens[k] = map[k]; }
        if (!tokens['bg.default'] && !tokens['fg.default']) return null;

        var body = document.body;
        var kind = body && body.classList.contains('vscode-high-contrast')
            ? 'high-contrast'
            : (body && body.classList.contains('vscode-dark')) ? 'dark' : 'light';

        return { kind: kind, tokens: tokens };
    }

    function postToIframe(iframeElem, type, payload) {
        if (!iframeElem || !iframeElem.contentWindow) return;
        // Isolated frames are cross-origin and cannot inherit the host page's CSS
        // variable cascade, so the theme must be pushed to them. When the host
        // resolves the theme on the page (VS Code webview), feed the live values on
        // mount and on every theme change so the frame stays in sync.
        if (type === 'verso/init' || type === 'verso/themeChanged') {
            var bundle = readVsCodeThemeBundle();
            if (bundle) {
                payload = payload || {};
                payload.theme = bundle;
            }
        }
        iframeElem.contentWindow.postMessage({ type: type, payload: payload }, '*');
    }

    function detach(handleId) {
        var entry = handles.get(handleId);
        if (!entry) return;
        window.removeEventListener('message', entry.listener);
        handles.delete(handleId);
    }

    var bridgeScriptPromise = null;
    function getBridgeScript() {
        if (bridgeScriptPromise) return bridgeScriptPromise;
        bridgeScriptPromise = fetch(bridgeUrl)
            .then(function (resp) {
                if (!resp.ok) throw new Error('verso bridge fetch failed: ' + resp.status);
                return resp.text();
            })
            .catch(function (err) {
                bridgeScriptPromise = null;
                throw err;
            });
        return bridgeScriptPromise;
    }

    window.versoLayoutFrame = {
        attach: attach,
        postToIframe: postToIframe,
        detach: detach,
        getBridgeScript: getBridgeScript
    };
})();
