// Parent-side interop for widget outputs.
//
// A widget output is a whole document rather than a fragment, so it is given a frame of its
// own rather than being pasted into the page. The document travels in the frame's srcdoc,
// with a small script spliced in ahead of it that does two jobs described below.
//
// Sizing runs the other way: the framed document reports how tall it turned out and the frame
// is set to match, because how much room a widget needs is only known after its own code has
// run.
(function () {
    'use strict';

    var DEFAULT_HEIGHT = 420;
    var MAX_HEIGHT = 2000;

    var handles = new Map();
    var nextId = 1;

    // Spliced in ahead of the widget so it runs first.
    //
    // The URL part is a workaround. A widget loader resolves paths against the document's own
    // location, and a framed document has no location that can be resolved against: the path
    // of about:srcdoc is opaque, so building a URL against it throws before anything is drawn.
    // Substituting a base that parses is enough to get past it, because what is being built at
    // that point is an address for a notebook server, which is not there to be reached in any
    // case. The substitute is on a reserved domain that cannot resolve, so nothing built from
    // it can reach anywhere either. Input that already resolves is passed through untouched.
    var FRAME_SCRIPT = [
        '<script>',
        '(function () {',
        '    var nativeUrl = window.URL;',
        '    function ForgivingUrl(url, base) {',
        '        try {',
        '            return new nativeUrl(url, base);',
        '        } catch (e) {',
        '            return new nativeUrl(url, "https://widget.invalid/");',
        '        }',
        '    }',
        '    ForgivingUrl.prototype = nativeUrl.prototype;',
        '    ["createObjectURL", "revokeObjectURL", "canParse", "parse"].forEach(function (name) {',
        '        if (nativeUrl[name]) { ForgivingUrl[name] = nativeUrl[name].bind(nativeUrl); }',
        '    });',
        '    window.URL = ForgivingUrl;',
        '',
        '    var last = -1;',
        '    function report() {',
        '        var el = document.documentElement;',
        '        var height = Math.max(el ? el.scrollHeight : 0, document.body ? document.body.scrollHeight : 0);',
        '        if (height > 0 && height !== last) {',
        '            last = height;',
        '            parent.postMessage({ type: "verso/widget-height", height: height }, "*");',
        '        }',
        '    }',
        '    window.addEventListener("load", report);',
        '    if (window.ResizeObserver) {',
        '        try { new ResizeObserver(report).observe(document.documentElement); } catch (e) { }',
        '    }',
        // A widget that fetches its own code settles some time after load, and a browser that
        // is not painting this frame reports nothing at all until it is. These catch both.
        '    setTimeout(report, 300); setTimeout(report, 1500); setTimeout(report, 5000);',
        '})();',
        '</script>'
    ].join('\n');

    function compose(html) {
        var head = /<head[^>]*>/i.exec(html);
        if (!head) return FRAME_SCRIPT + html;

        var at = head.index + head[0].length;
        return html.slice(0, at) + FRAME_SCRIPT + html.slice(at);
    }

    function mount(iframeElem, html) {
        if (!iframeElem || typeof html !== 'string') return null;

        var handleId = nextId++;

        var onMessage = function (event) {
            if (event.source !== iframeElem.contentWindow) return;
            var data = event.data;
            if (!data || data.type !== 'verso/widget-height') return;

            var height = Number(data.height);
            if (!isFinite(height) || height <= 0) return;
            iframeElem.style.height = Math.min(Math.round(height), MAX_HEIGHT) + 'px';
        };

        window.addEventListener('message', onMessage);
        handles.set(handleId, { listener: onMessage });

        iframeElem.style.height = DEFAULT_HEIGHT + 'px';
        iframeElem.srcdoc = compose(html);

        return handleId;
    }

    function detach(handleId) {
        var entry = handles.get(handleId);
        if (!entry) return;
        window.removeEventListener('message', entry.listener);
        handles.delete(handleId);
    }

    window.versoWidget = {
        mount: mount,
        detach: detach
    };
})();
