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

    // What a frame holds before its content has measured itself. Small enough that a widget grows
    // into place rather than collapsing into it, and not zero, so an output that is still loading
    // is somewhere on the page rather than nowhere.
    var INITIAL_HEIGHT = 28;

    // What a document that never measures itself is given, applied once the wait is clearly over.
    var DEFAULT_HEIGHT = 420;
    var UNSIZED_GRACE_MS = 6000;

    var MAX_HEIGHT = 2000;

    var handles = new Map();
    var nextId = 1;

    // Which frame is drawing which channel. Only frames given a channel id at mount appear here,
    // so a lookup that misses is the ordinary case of a message for an output that has since been
    // re-run or scrolled away, not an error.
    var framesByChannel = new Map();

    // Set by a host that receives channel traffic through Blazor interop. A host that talks to its
    // engine over the editor bridge leaves it null and the bridge is used instead.
    var channelHostRef = null;

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
        // Measured on the body rather than on the root element.
        //
        // A root element's scroll height is never less than the viewport, and the viewport here is
        // this frame, whose height is whatever was last reported from inside it. Measuring the root
        // therefore hands back the height the frame already had: it can grow and can never shrink,
        // so every widget shorter than the height a frame starts at keeps that height for good.
        //
        // The body's own height is what the content actually came to. Both readings of it are
        // taken because a widget that positions a part of itself out of the normal flow is taller
        // than its layout box says, and neither reading alone catches both.
        '    function report() {',
        '        var body = document.body;',
        '        if (!body) { return; }',
        '',
        '        var height = Math.max(body.scrollHeight, body.offsetHeight);',
        '        if (height > 0 && height !== last) {',
        '            last = height;',
        '            parent.postMessage({ type: "verso/widget-height", height: height }, "*");',
        '        }',
        '    }',
        '    window.addEventListener("load", report);',
        // This script runs in the head, where there is no body yet to watch. Waiting for one is
        // what makes the first accurate reading arrive as the content lays out rather than after
        // everything it loads has finished arriving, which for a widget is a slow difference.
        '    function watch() {',
        '        report();',
        '        if (!window.ResizeObserver || !document.body) { return; }',
        '        try { new ResizeObserver(report).observe(document.body); } catch (e) { }',
        '    }',
        '',
        '    if (document.readyState === "loading") {',
        '        document.addEventListener("DOMContentLoaded", watch);',
        '    } else {',
        '        watch();',
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

    // Spliced in alongside the script above, and only for a document whose output has a channel.
    //
    // The channel id is injected here rather than written into the document, which matters for two
    // separate reasons. A document is what gets saved, so an id inside it would be written to a
    // file and would name a channel that no longer exists the next time the file is opened. And a
    // document that never learns its own id cannot be rewritten to claim a different one, so the
    // only channel a frame can reach is the one it was mounted for.
    //
    // What the document gets is an object rather than a message vocabulary, because the vocabulary
    // is this file's business and an author writing a view should not have to reproduce it. The
    // handshake is left to the document to send: only the document knows when whatever it loaded
    // is able to receive, and the queue on the other side exists precisely to cover the gap.
    function channelScript(channelId) {
        return [
            '<script>',
            '(function () {',
            '    var channelId = ' + JSON.stringify(String(channelId)) + ';',
            '    var live = true;',
            '    var root = document.documentElement;',
            '    try { root.setAttribute("data-verso-live", "true"); } catch (e) { }',
            '',
            '    function fire(name, detail) {',
            '        try { window.dispatchEvent(new CustomEvent(name, { detail: detail })); }',
            '        catch (e) { }',
            '    }',
            '',
            '    window.versoChannel = {',
            '        id: channelId,',
            '        get isLive() { return live; },',
            // Announces that the view can receive, which flushes anything sent before it could.
            // The version is what the view was built against, and the host reports a mismatch
            // rather than refusing to draw.
            '        ready: function (protocolVersion) {',
            '            if (!live) { return false; }',
            '            parent.postMessage({',
            '                type: "verso/channel-ready",',
            '                channelId: channelId,',
            '                protocolVersion: protocolVersion || null',
            '            }, "*");',
            '            return true;',
            '        },',
            '        post: function (type, payload, buffers) {',
            '            if (!live || !type) { return false; }',
            '            parent.postMessage({',
            '                type: "verso/channel-message",',
            '                channelId: channelId,',
            '                messageType: String(type),',
            '                payload: payload === undefined ? null : payload,',
            '                buffers: buffers || null',
            '            }, "*");',
            '            return true;',
            '        }',
            '    };',
            '',
            '    window.addEventListener("message", function (event) {',
            '        var data = event.data;',
            '        if (!data || data.channelId !== channelId) { return; }',
            '',
            '        if (data.type === "verso/channel-post") {',
            '            fire("verso:channelmessage", {',
            '                type: data.messageType,',
            '                payload: data.payload === undefined ? null : data.payload,',
            '                buffers: data.buffers || null',
            '            });',
            '            return;',
            '        }',
            '',
            '        if (data.type === "verso/channel-closed") {',
            '            live = false;',
            '            try { root.setAttribute("data-verso-live", "false"); } catch (e) { }',
            '            fire("verso:channelclosed", { reason: data.reason || null });',
            '        }',
            '    });',
            '})();',
            '</script>'
        ].join('\n');
    }

    // Turns a widget document into a live one, and is spliced only for a document whose output
    // has a channel. A document without one is left to the loader it already carries, which draws
    // the state saved inside it and talks to nobody: that is what an exported page is, and what a
    // notebook opened without running anything should be.
    //
    // The widget front end the document loads is not only a renderer. It is the same bundle a
    // Jupyter front end uses, comm machinery included, and it expects a manager that can reach a
    // kernel. Supplying one is the whole of this: two methods over the channel, and the loader's
    // own code does the rest, including asking for every widget's current state as it mounts.
    //
    // That last part is why a live view is accurate rather than merely current-looking. A widget
    // opens its comm while it is being constructed, which is before it has been displayed and so
    // before any channel exists to carry the announcement. A view that waited to be told would
    // wait forever; a view that asks is told everything.
    function managerScript() {
        return [
            '<script>',
            '(function () {',
            '    var STATE_MIME = "application/vnd.jupyter.widget-state+json";',
            '    var VIEW_MIME = "application/vnd.jupyter.widget-view+json";',
            // What the state script is renamed to once this has taken it. The loader looks for
            // documents to draw by that type, so renaming it is what stops the saved state being
            // drawn underneath the live view a moment later.
            '    var CLAIMED_STATE_MIME = "application/vnd.verso.widget-state+json";',
            '    var WIDGET_TARGET = "jupyter.widget";',
            '    var CHANNEL_PROTOCOL = "1.0";',
            '',
            '    var channel = window.versoChannel;',
            '    if (!channel) { return; }',
            '',
            '    var comms = Object.create(null);',
            '    var counter = 0;',
            '',
            // What each message in flight is waiting to be told. The front end sends one change
            // at a time and merges everything that accumulates behind it into a single later
            // message, and what releases the next one is being told the last has been applied.
            // A message nobody is waiting on is not recorded, so this holds one entry per widget
            // being interacted with rather than one per message ever sent.
            '    var waiting = Object.create(null);',
            '',
            '    function complain(what, error) {',
            '        try { console.error("verso: " + what, error); } catch (e) { }',
            '    }',
            '',
            // Binary trait values travel as text in both directions, because the transport under
            // the channel is JSON the whole way to the interpreter.
            '    function decode(text) {',
            '        var binary = atob(text);',
            '        var bytes = new Uint8Array(binary.length);',
            '        for (var i = 0; i < binary.length; i++) { bytes[i] = binary.charCodeAt(i); }',
            '        return bytes.buffer;',
            '    }',
            '',
            '    function encode(part) {',
            '        var bytes = part instanceof ArrayBuffer',
            '            ? new Uint8Array(part)',
            '            : new Uint8Array(part.buffer, part.byteOffset || 0, part.byteLength);',
            '        var binary = "";',
            '        for (var i = 0; i < bytes.length; i++) { binary += String.fromCharCode(bytes[i]); }',
            '        return btoa(binary);',
            '    }',
            '',
            '    function encodeAll(parts) {',
            '        var out = [];',
            '        for (var i = 0; i < (parts || []).length; i++) {',
            '            try { out.push(encode(parts[i])); }',
            '            catch (e) { complain("a widget could not send part of its data", e); }',
            '        }',
            '        return out;',
            '    }',
            '',
            // The message shape the widget front end reads. It was written for a Jupyter kernel,
            // so it looks for a header and an envelope that this transport has no use for; they
            // are filled in rather than worked around, because the code that reads them is the
            // loader's and not ours.
            '    function envelope(body, msgType) {',
            '        var buffers = [];',
            '        var raw = body.buffers || [];',
            '        for (var i = 0; i < raw.length; i++) {',
            '            try { buffers.push(new DataView(decode(raw[i]))); }',
            '            catch (e) { complain("a widget could not read part of its data", e); }',
            '        }',
            '',
            '        return {',
            '            header: { msg_type: msgType },',
            '            parent_header: {},',
            '            metadata: body.metadata || {},',
            '            content: {',
            '                comm_id: body.comm_id,',
            '                data: body.data || {},',
            '                target_name: body.target_name || WIDGET_TARGET',
            '            },',
            '            buffers: buffers',
            '        };',
            '    }',
            '',
            '    function VersoComm(targetName, commId) {',
            '        this.comm_id = commId;',
            '        this.target_name = targetName;',
            '    }',
            '',
            '    VersoComm.prototype._post = function (msgType, data, metadata, buffers, callbacks) {',
            '        var messageId = "verso-" + (++counter);',
            '        var reply = callbacks && callbacks.iopub && callbacks.iopub.status;',
            '        if (reply) { waiting[messageId] = reply; }',
            '',
            '        channel.post("comm", {',
            '            comm_id: this.comm_id,',
            '            msg_type: msgType,',
            // Carried only when something is waiting on it, and answered only when it is
            // carried, so a message nobody is waiting on costs nothing on the way back.
            '            msg_id: reply ? messageId : null,',
            '            target_name: this.target_name,',
            '            data: data === undefined ? {} : data,',
            '            metadata: metadata === undefined ? {} : metadata,',
            '            buffers: encodeAll(buffers)',
            '        }, null);',
            '        return messageId;',
            '    };',
            '',
            '    VersoComm.prototype.open = function (data, callbacks, metadata, buffers) {',
            '        comms[this.comm_id] = this;',
            '        return this._post("comm_open", data, metadata, buffers, callbacks);',
            '    };',
            '',
            '    VersoComm.prototype.send = function (data, callbacks, metadata, buffers) {',
            '        return this._post("comm_msg", data, metadata, buffers, callbacks);',
            '    };',
            '',
            '    VersoComm.prototype.close = function (data, callbacks, metadata, buffers) {',
            '        delete comms[this.comm_id];',
            '        return this._post("comm_close", data, metadata, buffers, callbacks);',
            '    };',
            '',
            '    VersoComm.prototype.on_msg = function (handler) { this._onMessage = handler; };',
            '    VersoComm.prototype.on_close = function (handler) { this._onClose = handler; };',
            '',
            '    function deliver(manager, body) {',
            '        if (!body || !body.comm_id) { return; }',
            '',
            '        var msgType = body.msg_type || "comm_msg";',
            '',
            // A message of ours has been applied, which is what releases the next one. Reported
            // as the idle status a Jupyter kernel would raise, because that is the signal the
            // front end is written against and this transport has no execution state of its own.
            '        if (msgType === "comm_ack") {',
            '            var reply = waiting[body.msg_id];',
            '            if (!reply) { return; }',
            '            delete waiting[body.msg_id];',
            '            try { reply({ content: { execution_state: "idle" } }); }',
            '            catch (e) { complain("a widget could not be told its message landed", e); }',
            '            return;',
            '        }',
            '',
            '        var message = envelope(body, msgType);',
            '',
            '        if (msgType === "comm_open") {',
            '            var opened = new VersoComm(body.target_name || WIDGET_TARGET, body.comm_id);',
            '            comms[body.comm_id] = opened;',
            '            var handled = manager.handle_comm_open(opened, message);',
            '            if (handled && handled.catch) {',
            '                handled.catch(function (error) { complain("a widget could not be opened", error); });',
            '            }',
            '            return;',
            '        }',
            '',
            // A message for a widget this view does not draw. Every view is told about every
            // widget in the session, because nothing outside a view can tell which models it
            // took, so arriving here is ordinary rather than a fault.
            '        var comm = comms[body.comm_id];',
            '        if (!comm) { return; }',
            '',
            '        if (msgType === "comm_close") {',
            '            delete comms[body.comm_id];',
            '            if (comm._onClose) { comm._onClose(message); }',
            '            return;',
            '        }',
            '',
            '        if (comm._onMessage) { comm._onMessage(message); }',
            '    }',
            '',
            // Taken before the loader looks for it, so the saved state is this view's fallback
            // rather than a second widget drawn under the live one.
            '    function claimState() {',
            '        var script = document.querySelector("script[type=\'" + STATE_MIME + "\']");',
            '        if (!script) { return null; }',
            '',
            '        script.setAttribute("type", CLAIMED_STATE_MIME);',
            '        try { return JSON.parse(script.textContent); }',
            '        catch (e) { complain("a widget document carried state that could not be read", e); return null; }',
            '    }',
            '',
            '    function viewSlots() {',
            '        var slots = [];',
            '        var scripts = document.querySelectorAll("script[type=\'" + VIEW_MIME + "\']");',
            '',
            '        for (var i = 0; i < scripts.length; i++) {',
            '            var script = scripts[i];',
            '            var spec;',
            '            try { spec = JSON.parse(script.textContent); } catch (e) { continue; }',
            '            if (!spec || !spec.model_id || !script.parentElement) { continue; }',
            '',
            // The still image some embedders leave in front of a view, so a page that never runs
            // shows something. This one is about to run.
            '            var placeholder = script.previousElementSibling;',
            '            if (placeholder && placeholder.tagName === "IMG"',
            '                && placeholder.classList.contains("jupyter-widget")) {',
            '                script.parentElement.removeChild(placeholder);',
            '            }',
            '',
            '            var host = document.createElement("div");',
            '            host.className = "widget-subarea";',
            '            script.parentElement.insertBefore(host, script);',
            '            slots.push({ modelId: spec.model_id, host: host });',
            '        }',
            '',
            '        return slots;',
            '    }',
            '',
            '    function start() {',
            '        var saved = claimState();',
            '        var slots = viewSlots();',
            '        if (!slots.length) { return; }',
            '',
            '        require([',
            '            "@jupyter-widgets/base",',
            '            "@jupyter-widgets/html-manager",',
            '            "@jupyter-widgets/html-manager/dist/libembed-amd"',
            '        ], function (base, html, libembed) {',
            // Two methods, and the loader's own manager supplies everything else, including the
            // conversation that pulls every widget's state as this mounts.
            '            class VersoWidgetManager extends html.HTMLManager {',
            '                _create_comm(targetName, commId, data, metadata, buffers) {',
            '                    var comm = new VersoComm(targetName, commId || ("verso-comm-" + (++counter)));',
            '                    comms[comm.comm_id] = comm;',
            // Only an announcement carries something to announce. Asked for a comm to a widget
            // that already exists, opening one would tell the interpreter to build a second.
            '                    if (data !== undefined || metadata !== undefined) {',
            '                        comm.open(data, undefined, metadata, buffers);',
            '                    }',
            '                    return Promise.resolve(comm);',
            '                }',
            '',
            '                _get_comm_info() {',
            '                    var info = {};',
            '                    for (var id in comms) {',
            '                        if (comms[id].target_name === WIDGET_TARGET) { info[id] = {}; }',
            '                    }',
            '                    return Promise.resolve(info);',
            '                }',
            '            }',
            '',
            '            var manager = new VersoWidgetManager({ loader: libembed.requireLoader });',
            '',
            '            window.addEventListener("verso:channelmessage", function (event) {',
            '                var detail = event.detail || {};',
            '                if (detail.type !== "comm") { return; }',
            '                try { deliver(manager, detail.payload); }',
            '                catch (e) { complain("a widget message could not be applied", e); }',
            '            });',
            '',
            // Announced before anything is asked for, because announcing is what releases the
            // messages held while this document was still loading.
            '            channel.ready(CHANNEL_PROTOCOL);',
            '',
            '            Promise.resolve(manager._loadFromKernel()).catch(function (error) {',
            '                complain("a widget could not read its state from the kernel", error);',
            '            }).then(function () {',
            // Checked rather than caught. An interpreter that has gone away is reported as a
            // timeout by one path and as an empty answer by another, and only one of the two
            // arrives as a rejection, so what matters is whether the models turned up.
            '                var missing = slots.filter(function (slot) { return !manager.get_model(slot.modelId); });',
            '                if (!missing.length || !saved) { return; }',
            '                return manager.set_state(saved);',
            '            }).then(function () {',
            '                return draw(manager, slots);',
            '            }).catch(function (error) {',
            '                complain("a widget could not be drawn", error);',
            '            });',
            '        }, function (error) {',
            '            complain("the widget front end could not be loaded", error);',
            '        });',
            '    }',
            '',
            '    function draw(manager, slots) {',
            '        return Promise.all(slots.map(function (slot) {',
            '            var model = manager.get_model(slot.modelId);',
            '            if (!model) { return null; }',
            '            return Promise.resolve(model).then(function (resolved) {',
            '                return manager.create_view(resolved);',
            '            }).then(function (view) {',
            '                return manager.display_view(view, slot.host);',
            '            }).catch(function (error) {',
            '                complain("a widget view could not be drawn", error);',
            '            });',
            '        }));',
            '    }',
            '',
            // The loader is a plain script at the end of the body, so it has run by the time the
            // document is parsed and has not yet run while this is being spliced into the head.
            '    if (document.readyState === "loading") {',
            '        document.addEventListener("DOMContentLoaded", start);',
            '    } else {',
            '        start();',
            '    }',
            '})();',
            '</script>'
        ].join('\n');
    }

    function compose(html, theme, channelId) {
        var injected = themeStyleTag(theme) + FRAME_SCRIPT
            + (channelId ? channelScript(channelId) + managerScript() : '');
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

    // A channel's traffic on its way to whoever owns the channel, which is somewhere in the engine
    // rather than on this page. The two routes are the two ways a page reaches its engine, and are
    // the same two the cell-interaction bridge already chooses between.
    //
    // Nothing here is allowed to throw. A view that loses a message is worse off than one that
    // never sent it, but a rejected promise escaping a message listener takes the page's console
    // with it and tells the user nothing.
    function routeToOwner(method, bridgeMethod, params, args) {
        if (window.vscodeBridge && typeof window.vscodeBridge.sendRequest === 'function') {
            try {
                var sent = window.vscodeBridge.sendRequest(bridgeMethod, JSON.stringify(params));
                if (sent && typeof sent.catch === 'function') {
                    sent.catch(function (error) { warn(bridgeMethod, error); });
                }
            } catch (e) {
                warn(bridgeMethod, e);
            }
            return;
        }

        if (!channelHostRef) return;

        try {
            var invoked = channelHostRef.invokeMethodAsync.apply(
                channelHostRef, [method].concat(args));
            if (invoked && typeof invoked.catch === 'function') {
                invoked.catch(function (error) { warn(method, error); });
            }
        } catch (e) {
            warn(method, e);
        }
    }

    function warn(what, error) {
        try { console.error('verso: an output channel could not reach the host through ' + what, error); }
        catch (e) { }
    }

    // The document hands over whatever shape it likes and the owner is given JSON text, because the
    // owner knows what it expects back and nothing in between does.
    function payloadText(payload) {
        if (payload === null || payload === undefined) return null;
        try { return JSON.stringify(payload); }
        catch (e) { return null; }
    }

    // Binary travels as base64 strings, which both the editor bridge and the Blazor boundary carry
    // as ordinary JSON. Anything that is not a string is dropped rather than sent as something the
    // other side would have to guess at.
    function bufferList(buffers) {
        if (!buffers || !buffers.length) return null;
        var out = [];
        for (var i = 0; i < buffers.length; i++) {
            if (typeof buffers[i] === 'string') out.push(buffers[i]);
        }
        return out.length ? out : null;
    }

    function mount(iframeElem, html, themeKind, channelId) {
        if (!iframeElem || typeof html !== 'string') return null;

        var handleId = nextId++;
        var channel = channelId ? String(channelId) : null;

        var onMessage = function (event) {
            if (event.source !== iframeElem.contentWindow) return;
            var data = event.data;
            if (!data) return;

            if (data.type === 'verso/widget-download') {
                deliverDownload(data);
                return;
            }

            // A frame speaks only for the channel it was mounted with. It was never told any other
            // id, so this is a consistency check rather than a defence: it is here so a document
            // that gets its own bookkeeping wrong stops here instead of quietly addressing an
            // output belonging to someone else.
            if (data.type === 'verso/channel-ready') {
                if (channel && data.channelId === channel) {
                    routeToOwner(
                        'OnOutputChannelReady', 'channel/ready',
                        { channelId: channel, protocolVersion: data.protocolVersion || null },
                        [channel, data.protocolVersion || null]);
                }
                return;
            }

            if (data.type === 'verso/channel-message') {
                if (channel && data.channelId === channel && data.messageType) {
                    var text = payloadText(data.payload);
                    var buffers = bufferList(data.buffers);
                    routeToOwner(
                        'OnOutputChannelMessage', 'channel/message',
                        {
                            channelId: channel,
                            messageType: String(data.messageType),
                            payload: text,
                            buffers: buffers
                        },
                        [channel, String(data.messageType), text, buffers]);
                }
                return;
            }

            if (data.type !== 'verso/widget-height') return;

            var height = Number(data.height);
            if (!isFinite(height) || height <= 0) return;

            unsized = clearTimeout(unsized);
            iframeElem.style.height = Math.min(Math.round(height), MAX_HEIGHT) + 'px';
        };

        window.addEventListener('message', onMessage);

        // A widget's code is fetched before anything can be drawn, so the gap between the frame
        // appearing and the first measurement arriving is long enough to see. Started small, the
        // frame grows into what the content came to, which reads as something arriving. Started
        // at a guess, it would stand empty at the wrong size and then collapse, which reads as a
        // fault.
        //
        // The guess is still what a document that never measures itself falls back to, which is
        // what a widget whose code did not load leaves behind. It is applied once, late, so it
        // cannot be what the eye lands on first.
        var unsized = setTimeout(function () {
            unsized = null;
            iframeElem.style.height = DEFAULT_HEIGHT + 'px';
        }, UNSIZED_GRACE_MS);

        handles.set(handleId, {
            listener: onMessage,
            frame: iframeElem,
            kind: themeKind,
            channelId: channel,
            cancelFallback: function () { unsized = clearTimeout(unsized); }
        });

        // Last mount wins. A view that unmounts and mounts again keeps its channel, and the entry
        // left by the frame it replaced would otherwise point at an iframe that is no longer drawn.
        if (channel) framesByChannel.set(channel, handleId);

        iframeElem.style.height = INITIAL_HEIGHT + 'px';
        iframeElem.srcdoc = compose(html, readTheme(iframeElem, themeKind), channel);

        return handleId;
    }

    function frameForChannel(channelId) {
        var handleId = framesByChannel.get(String(channelId));
        if (handleId === undefined) return null;

        var entry = handles.get(handleId);
        var frame = entry && entry.frame;
        return (frame && frame.contentWindow) ? frame : null;
    }

    // Delivers one of the channel owner's messages into the frame drawing it. Returns whether there
    // was a frame to deliver it to, which is what tells the host a view has gone without saying so.
    //
    // The ext/ prefix comes off here, which is the last hop it is useful on. It exists to keep the
    // framework's own namespace unmistakable on the way through the host, and inside a message
    // already typed verso/channel-post there is nothing left for it to disambiguate. Taking it off
    // is what makes both ends see the type the other sent: the owner is handed back what the view
    // posted, and the view is handed what the owner posted.
    function postToChannel(channelId, messageType, payload, buffers) {
        var frame = frameForChannel(channelId);
        if (!frame || !messageType) return false;

        var type = String(messageType);
        if (type.indexOf('ext/') === 0) type = type.slice(4);

        try {
            frame.contentWindow.postMessage({
                type: 'verso/channel-post',
                channelId: String(channelId),
                messageType: type,
                payload: payload === undefined ? null : payload,
                buffers: buffers || null
            }, '*');
            return true;
        } catch (e) {
            // The frame went away between the lookup and the post.
            return false;
        }
    }

    // Tells a frame its channel is gone, so it can stop expecting answers and show that it is no
    // longer live. The binding is kept: the frame is still on the page and still its own view.
    function closeChannel(channelId, reason) {
        var frame = frameForChannel(channelId);
        if (!frame) return false;

        // Marked on the frame as well as inside the document. The page around it styles itself
        // from this attribute and reveals the note explaining that the widget is showing saved
        // state, so the whole visible treatment follows from one attribute changing.
        try { frame.setAttribute('data-verso-live', 'false'); } catch (e) { }

        try {
            frame.contentWindow.postMessage({
                type: 'verso/channel-closed',
                channelId: String(channelId),
                reason: reason || null
            }, '*');
            return true;
        } catch (e) {
            return false;
        }
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
        if (entry.cancelFallback) entry.cancelFallback();
        handles.delete(handleId);

        // Only if this frame is still the one drawing the channel. A view that remounted has
        // already claimed it, and its own entry must survive the old frame being torn down.
        if (entry.channelId && framesByChannel.get(entry.channelId) === handleId) {
            framesByChannel.delete(entry.channelId);
        }
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

    // The host's half of an output channel. Kept apart from versoWidget because it is addressed by
    // channel rather than by frame: the engine knows which conversation it is having and not which
    // element on the page happens to be drawing it.
    window.versoOutputChannel = {
        // Called once by a host that receives channel traffic through Blazor interop.
        register: function (dotNetRef) { channelHostRef = dotNetRef; },
        post: postToChannel,
        closed: closeChannel
    };
})();
