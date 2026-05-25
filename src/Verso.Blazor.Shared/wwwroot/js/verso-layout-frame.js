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

    function postToIframe(iframeElem, type, payload) {
        if (!iframeElem || !iframeElem.contentWindow) return;
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
        bridgeScriptPromise = fetch('_content/Verso.Blazor.Shared/js/verso-layout-bridge.js')
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
