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

    // Theme values a framed document may want, named as the host page names them.
    //
    // A framed document is on an opaque origin and gets no part of the page's cascade, so a
    // widget that wants to match the notebook cannot read these for itself. They are resolved
    // where the frame sits in the page, which is inside the cell's output, so a layout that
    // narrows a value for its own output area is honoured, and handed to the document as custom
    // properties of the same names. What the host page derives them from differs per host: in a
    // VS Code webview they map onto the active editor theme, and on a served page the notebook
    // layout defines them from the active Verso theme. Either way these are the live values.
    var THEME_COLOR_TOKENS = [
        '--verso-cell-output-background',
        '--verso-cell-output-foreground',
        '--verso-border-default',
        '--verso-bg-default',
        '--verso-fg-default',
        '--verso-fg-muted',
        '--verso-accent-primary'
    ];

    var THEME_TEXT_TOKENS = [
        '--verso-ui-font-family',
        '--verso-ui-font-size',
        '--verso-font-family-mono'
    ];

    var colorProbe = null;
    var colorCanvas = null;

    // Resolves a color token to a plain rgb()/rgba() value.
    //
    // A custom property is not a color as far as the page is concerned, it is a token sequence, so
    // what comes back from getComputedStyle can be anything a color is allowed to be written as.
    // That matters twice over. One host defines these through color-mix(), and the engine reports
    // what that resolves to in a modern notation, color(srgb ...), which is a perfectly good color
    // and still not one an arbitrary widget's color parser will accept.
    //
    // So the value is put through two steps. Assigning it to a real color property resolves
    // anything that only has meaning against an element, and rejects a value that is not a color at
    // all, which is how an unusable token is told apart from a usable one. Painting the result and
    // reading the pixel back then reduces every notation to channels, which is the one form
    // everything downstream understands.
    function resolveColor(value) {
        if (!value) return null;

        if (!colorProbe) {
            colorProbe = document.createElement('span');
            colorProbe.setAttribute('aria-hidden', 'true');
            colorProbe.style.display = 'none';
            document.body.appendChild(colorProbe);
        }

        var used;
        try {
            colorProbe.style.color = '';
            colorProbe.style.color = value;
            if (!colorProbe.style.color) return null;
            used = window.getComputedStyle(colorProbe).color;
        } catch (e) {
            return null;
        }
        if (!used) return null;

        try {
            if (!colorCanvas) {
                colorCanvas = document.createElement('canvas');
                colorCanvas.width = 1;
                colorCanvas.height = 1;
            }
            var ctx = colorCanvas.getContext('2d');
            ctx.clearRect(0, 0, 1, 1);
            ctx.fillStyle = used;
            ctx.fillRect(0, 0, 1, 1);
            var px = ctx.getImageData(0, 0, 1, 1).data;
            return px[3] >= 255
                ? 'rgb(' + px[0] + ', ' + px[1] + ', ' + px[2] + ')'
                : 'rgba(' + px[0] + ', ' + px[1] + ', ' + px[2] + ', ' + (px[3] / 255).toFixed(3) + ')';
        } catch (e) {
            // Reading a pixel back is the belt to the braces above; the used value still stands.
            return used.trim();
        }
    }

    // Reads the theme as it applies at the frame's position in the page. Returns null when
    // nothing resolves, so a host that defines none of them adds nothing to the document.
    function readTheme(element, kindHint) {
        var style;
        try { style = window.getComputedStyle(element); }
        catch (e) { return null; }
        if (!style) return null;

        var values = {};
        var any = false;

        for (var i = 0; i < THEME_COLOR_TOKENS.length; i++) {
            var colorName = THEME_COLOR_TOKENS[i];
            var resolved = resolveColor((style.getPropertyValue(colorName) || '').trim());
            if (!resolved) continue;
            values[colorName] = resolved;
            any = true;
        }

        for (var j = 0; j < THEME_TEXT_TOKENS.length; j++) {
            var textName = THEME_TEXT_TOKENS[j];
            var text = (style.getPropertyValue(textName) || '').trim();
            if (!text) continue;
            values[textName] = text;
            any = true;
        }

        if (!any) return null;

        return { kind: themeKind(kindHint, values['--verso-cell-output-background']), values: values };
    }

    // Light or dark, by whichever signal the host offers. A VS Code webview says so on the body.
    // Otherwise the background the widget will sit against answers it, which holds on any host and
    // needs nothing threaded through from the notebook.
    function themeKind(kindHint, background) {
        var body = document.body;
        if (body && body.classList.contains('vscode-high-contrast')) return 'high-contrast';
        if (body && body.classList.contains('vscode-dark')) return 'dark';
        if (body && body.classList.contains('vscode-light')) return 'light';
        if (kindHint) return kindHint;

        var channels = background && background.match(/[\d.]+/g);
        if (channels && channels.length >= 3) {
            var luminance = (0.2126 * +channels[0] + 0.7152 * +channels[1] + 0.0722 * +channels[2]) / 255;
            return luminance < 0.5 ? 'dark' : 'light';
        }

        return 'light';
    }

    function themeStyleTag(theme) {
        if (!theme) return '';
        var declarations = '';
        for (var name in theme.values) {
            if (!Object.prototype.hasOwnProperty.call(theme.values, name)) continue;
            declarations += name + ':' + theme.values[name] + ';';
        }
        declarations += '--verso-theme-kind:' + theme.kind + ';';
        return '<style>:root{' + declarations + '}</style>';
    }

    // Spliced in ahead of the widget so it runs first.
    //
    // The URL part is a workaround. A widget loader resolves paths against the document's own
    // location, and a framed document has no location that can be resolved against: the path
    // of about:srcdoc is opaque, so building a URL against it throws before anything is drawn.
    // Substituting a base that parses is enough to get past it, because what is being built at
    // that point is an address for a notebook server, which is not there to be reached in any
    // case. The substitute is on a reserved domain that cannot resolve, so nothing built from
    // it can reach anywhere either. Input that already resolves is passed through untouched.
    //
    // The download part is the reason a widget's own save button does anything at all. A frame
    // is sandboxed, and a download started inside a sandbox is refused, so a chart's snapshot
    // button or a plot's screenshot button would otherwise do nothing at all. The click is taken
    // here instead and the bytes handed out to the page, which is not sandboxed and can either
    // save the file itself or pass it to an editor that wants to ask where it should go.
    //
    // The hook is the anchor rather than any one library's button, because building a blob and
    // clicking a link at it is how all of them do this. It has to replace the method rather than
    // listen for the event: the anchor is generally never added to the document, so its click
    // reaches no listener anywhere.
    //
    // Both ways of clicking one are taken. Some libraries call click(); others build the event
    // themselves and dispatch it, which runs the same download without ever calling the method.
    // Shadowing dispatchEvent on the anchor prototype catches the second without touching how
    // events are dispatched at anything else.
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
        // Only what the widget built for itself is taken. A link to somewhere else is left to
        // behave as a link, and could not be read from here in any case.
        '    function savedByWidget(anchor) {',
        '        if (!anchor || !anchor.getAttribute) { return null; }',
        '        var name = anchor.getAttribute("download");',
        '        var href = anchor.href;',
        '        var local = href && (href.indexOf("blob:") === 0 || href.indexOf("data:") === 0);',
        '        return (name && local) ? { name: name, href: href } : null;',
        '    }',
        '',
        '    function handOut(request, letTheFrameTry) {',
        '        fetch(request.href).then(function (response) {',
        '            return response.blob();',
        '        }).then(function (blob) {',
        '            return new Promise(function (resolve, reject) {',
        '                var reader = new FileReader();',
        '                reader.onload = function () { resolve({ type: blob.type, result: String(reader.result) }); };',
        '                reader.onerror = function () { reject(reader.error); };',
        '                reader.readAsDataURL(blob);',
        '            });',
        '        }).then(function (read) {',
        '            var comma = read.result.indexOf(",");',
        '            parent.postMessage({',
        '                type: "verso/widget-download",',
        '                fileName: request.name,',
        '                contentType: read.type || "application/octet-stream",',
        '                data: comma >= 0 ? read.result.slice(comma + 1) : read.result',
        '            }, "*");',
        '        }).catch(function (error) {',
        // Nothing was handed out, so give the click back rather than swallowing it. The sandbox
        // will most likely refuse the download, but failing the way it did before this existed
        // beats failing silently in a new way.
        '            try { console.error("verso: could not read a download, letting the frame try it", error); } catch (e) { }',
        '            try { letTheFrameTry(); } catch (e) { }',
        '        });',
        '    }',
        '',
        '    var nativeAnchorClick = HTMLAnchorElement.prototype.click;',
        '    HTMLAnchorElement.prototype.click = function () {',
        '        var request = savedByWidget(this);',
        '        if (!request) { return nativeAnchorClick.apply(this, arguments); }',
        '',
        '        var anchor = this;',
        '        var args = arguments;',
        '        handOut(request, function () { nativeAnchorClick.apply(anchor, args); });',
        '    };',
        '',
        '    var nativeDispatch = HTMLAnchorElement.prototype.dispatchEvent;',
        '    HTMLAnchorElement.prototype.dispatchEvent = function (event) {',
        '        var request = (event && event.type === "click") ? savedByWidget(this) : null;',
        '        if (!request) { return nativeDispatch.apply(this, arguments); }',
        '',
        '        var anchor = this;',
        '        var args = arguments;',
        '        handOut(request, function () { nativeDispatch.apply(anchor, args); });',
        // The event was not cancelled, which is what a dispatch of it would have reported.
        '        return true;',
        '    };',
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
        // The theme travels into the document twice: as custom properties written straight into
        // the markup, so they are already set before anything draws, and again on every later
        // change. Content that has drawn itself in colors it read once cannot be restyled from
        // out here, so the change is announced and the document decides what to redraw.
        '    window.addEventListener("message", function (event) {',
        '        var data = event.data;',
        '        if (!data || data.type !== "verso/widget-theme" || !data.values) { return; }',
        '        var root = document.documentElement;',
        '        for (var name in data.values) {',
        '            if (!Object.prototype.hasOwnProperty.call(data.values, name)) { continue; }',
        '            try { root.style.setProperty(name, data.values[name]); } catch (e) { }',
        '        }',
        '        if (data.kind) {',
        '            try { root.style.setProperty("--verso-theme-kind", data.kind); } catch (e) { }',
        '        }',
        '        try {',
        '            window.dispatchEvent(new CustomEvent("verso:themechanged", { detail: { kind: data.kind } }));',
        '        } catch (e) { }',
        '        report();',
        '    });',
        '})();',
        '</script>'
    ].join('\n');

    function compose(html, theme) {
        var injected = themeStyleTag(theme) + FRAME_SCRIPT;
        var head = /<head[^>]*>/i.exec(html);
        if (!head) return injected + html;

        var at = head.index + head[0].length;
        return html.slice(0, at) + injected + html.slice(at);
    }

    // Delivers a file a widget asked to save. The page is not sandboxed, so this is where a
    // download can actually happen.
    //
    // Inside an editor the bytes are handed over rather than saved, because the editor owns the
    // question of where a file goes and whether the user still wants it; a cancelled dialog then
    // writes nothing. Everywhere else the page saves it the same way the toolbar's own exports
    // do, which keeps the bytes in the browser rather than sending them anywhere.
    function deliverDownload(request) {
        if (!request || !request.fileName || !request.data) return;

        var fileName = String(request.fileName);
        var contentType = String(request.contentType || 'application/octet-stream');
        var data = String(request.data);

        if (window.vscodeBridge && typeof window.vscodeBridge.sendRequest === 'function') {
            try {
                window.vscodeBridge.sendRequest('file/save', JSON.stringify({
                    fileName: fileName,
                    contentType: contentType,
                    data: data
                }));
                return;
            } catch (e) {
                // Fall through: a page that can save it itself is better than losing the file.
            }
        }

        if (window.versoFileDownload && typeof window.versoFileDownload.triggerDownload === 'function') {
            try { window.versoFileDownload.triggerDownload(fileName, contentType, data); }
            catch (e) { try { console.error('verso: download failed', e); } catch (_) { } }
        }
    }

    function mount(iframeElem, html, themeKind) {
        if (!iframeElem || typeof html !== 'string') return null;

        var handleId = nextId++;

        var onMessage = function (event) {
            if (event.source !== iframeElem.contentWindow) return;
            var data = event.data;
            if (!data) return;

            if (data.type === 'verso/widget-download') {
                deliverDownload(data);
                return;
            }

            if (data.type !== 'verso/widget-height') return;

            var height = Number(data.height);
            if (!isFinite(height) || height <= 0) return;
            iframeElem.style.height = Math.min(Math.round(height), MAX_HEIGHT) + 'px';
        };

        window.addEventListener('message', onMessage);
        handles.set(handleId, { listener: onMessage, frame: iframeElem, kind: themeKind });

        iframeElem.style.height = DEFAULT_HEIGHT + 'px';
        iframeElem.srcdoc = compose(html, readTheme(iframeElem, themeKind));

        return handleId;
    }

    // Re-reads the theme where the frame sits and posts it in. Called when the notebook's theme
    // changes, so a document already drawn can follow rather than waiting to be re-run.
    function retheme(handleId, themeKind) {
        var entry = handles.get(handleId);
        if (!entry || !entry.frame || !entry.frame.contentWindow) return;

        if (themeKind) entry.kind = themeKind;

        var theme = readTheme(entry.frame, entry.kind);
        if (!theme) return;

        try {
            entry.frame.contentWindow.postMessage(
                { type: 'verso/widget-theme', kind: theme.kind, values: theme.values }, '*');
        } catch (e) { /* frame went away between the check and the post */ }
    }

    function rethemeAll() {
        handles.forEach(function (_entry, handleId) { retheme(handleId); });
    }

    function detach(handleId) {
        var entry = handles.get(handleId);
        if (!entry) return;
        window.removeEventListener('message', entry.listener);
        handles.delete(handleId);
    }

    // A VS Code webview restates the theme on the body when the editor theme changes, and swaps
    // the values every custom property here is mapped from at the same moment. Watching the class
    // is enough to notice, and keeps already-drawn widgets in step without them being re-run.
    function watchHostTheme() {
        if (!window.MutationObserver || !document.body) return;
        try {
            new MutationObserver(function () {
                if (handles.size > 0) rethemeAll();
            }).observe(document.body, { attributes: true, attributeFilter: ['class'] });
        } catch (e) { /* observation is an improvement, not a requirement */ }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', watchHostTheme);
    } else {
        watchHostTheme();
    }

    window.versoWidget = {
        mount: mount,
        retheme: retheme,
        rethemeAll: rethemeAll,
        detach: detach
    };
})();
