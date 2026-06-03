// In-iframe bridge shim for isolated layout renderers. The host injects this
// script into every isolated-layout iframe's srcdoc. It exposes a small `verso`
// global that the extension's entry-point module uses to communicate with the
// host. Outbound messages reach the host via window.parent.postMessage; inbound
// messages reach the renderer via the handler registered through verso.onMessage.
//
// The host calls window.versoBridgeInit({extensionId, layoutId, frameInstanceId})
// before importing the entry-point module so identity is captured up front and
// stamped onto every outbound verso/ready and verso/* envelope. Extensions cannot
// override these identity fields from inside the frame.

(function () {
    var bound = null;
    var inboundHandlers = [];
    var firstInbound = false;
    var readyTimer = null;

    // Applies a resolved theme bundle ({ kind, tokens }) to the iframe's :root.
    // Token keys use dotted notation ("bg.default") and are converted to the
    // host's CSS custom-property naming ("--verso-bg-default") so iframe
    // stylesheets can reference the same variables as the host page.
    function applyTheme(theme) {
        if (!theme || !theme.tokens || typeof theme.tokens !== 'object') return;
        var root = document.documentElement;
        var tokens = theme.tokens;
        for (var key in tokens) {
            if (!Object.prototype.hasOwnProperty.call(tokens, key)) continue;
            var value = tokens[key];
            if (typeof value !== 'string') continue;
            var cssName = '--verso-' + key.replace(/\./g, '-');
            try { root.style.setProperty(cssName, value); } catch (_) {}
        }
        if (typeof theme.kind === 'string' && theme.kind.length > 0) {
            try { root.style.setProperty('--verso-theme-kind', theme.kind); } catch (_) {}
            try { root.setAttribute('data-verso-theme-kind', theme.kind); } catch (_) {}
        }
    }

    window.addEventListener('message', function (event) {
        var data = event.data;
        if (!data || typeof data !== 'object' || typeof data.type !== 'string') return;
        // The first valid inbound message proves the host has wired up its listener
        // and is answering, so stop re-announcing readiness (see ready()).
        if (!firstInbound) {
            firstInbound = true;
            if (readyTimer !== null) { clearInterval(readyTimer); readyTimer = null; }
        }
        if ((data.type === 'verso/init' || data.type === 'verso/themeChanged')
            && data.payload && data.payload.theme) {
            try { applyTheme(data.payload.theme); }
            catch (e) {
                try { console.error('verso bridge applyTheme error:', e); } catch (_) {}
            }
        }
        for (var i = 0; i < inboundHandlers.length; i++) {
            try {
                inboundHandlers[i](data.type, data.payload);
            } catch (e) {
                // Don't let a handler exception block the others.
                try { console.error('verso bridge handler error:', e); } catch (_) {}
            }
        }
    });

    function ensureBound() {
        if (!bound) {
            throw new Error('verso bridge has not been initialized; verso.ready() called before versoBridgeInit.');
        }
    }

    function postReady() {
        window.parent.postMessage({
            type: 'verso/ready',
            payload: {
                extensionId: bound.extensionId,
                layoutId: bound.layoutId,
                frameInstanceId: bound.frameInstanceId
            }
        }, '*');
    }

    function ready() {
        ensureBound();
        postReady();

        // The host registers its message listener after the iframe element renders.
        // If this frame loads and posts a single ready() before that listener exists,
        // the signal is lost and no verso/init ever arrives. Re-announce on an interval
        // until the first inbound message confirms the host is answering. Capped so a
        // genuinely unresponsive host still stops (the host applies its own ready
        // timeout independently).
        if (firstInbound || readyTimer !== null) return;
        var attempts = 0;
        readyTimer = setInterval(function () {
            if (firstInbound || ++attempts >= 25) {
                clearInterval(readyTimer);
                readyTimer = null;
                return;
            }
            postReady();
        }, 200);
    }

    function send(type, payload) {
        if (typeof type !== 'string' || type.length === 0) {
            throw new Error('verso.send: type must be a non-empty string.');
        }
        if (type.indexOf('verso/') === 0) {
            throw new Error('verso.send: type "' + type + '" is in the reserved verso/ namespace.');
        }
        window.parent.postMessage({ type: type, payload: payload }, '*');
    }

    function onMessage(handler) {
        if (typeof handler !== 'function') {
            throw new Error('verso.onMessage: handler must be a function.');
        }
        inboundHandlers.push(handler);
    }

    // Layout interaction. The bridge stamps identity (extensionId/layoutId/
    // frameInstanceId) from the mount-bound values; the frame cannot spoof
    // another layout.
    function interact(interactionType, payload, targetId) {
        ensureBound();
        if (typeof interactionType !== 'string' || interactionType.length === 0) {
            throw new Error('verso.interact: interactionType must be a non-empty string.');
        }
        window.parent.postMessage({
            type: 'verso/interact',
            payload: {
                extensionId: bound.extensionId,
                layoutId: bound.layoutId,
                frameInstanceId: bound.frameInstanceId,
                interactionType: interactionType,
                payload: payload,
                targetId: typeof targetId === 'string' ? targetId : null
            }
        }, '*');
    }

    // Cell interaction. extensionId is stamped from bound identity; the frame
    // supplies cellId, interactionType, payload, and optional region/outputBlockId.
    function cellInteract(cellId, interactionType, payload, options) {
        ensureBound();
        if (typeof cellId !== 'string' || cellId.length === 0) {
            throw new Error('verso.cellInteract: cellId must be a non-empty string.');
        }
        if (typeof interactionType !== 'string' || interactionType.length === 0) {
            throw new Error('verso.cellInteract: interactionType must be a non-empty string.');
        }
        var opts = options || {};
        window.parent.postMessage({
            type: 'verso/cellInteract',
            payload: {
                cellId: cellId,
                extensionId: bound.extensionId,
                interactionType: interactionType,
                payload: payload,
                outputBlockId: typeof opts.outputBlockId === 'string' ? opts.outputBlockId : null,
                region: typeof opts.region === 'string' ? opts.region : null
            }
        }, '*');
    }

    function executeCell(cellId) {
        ensureBound();
        if (typeof cellId !== 'string' || cellId.length === 0) {
            throw new Error('verso.executeCell: cellId must be a non-empty string.');
        }
        window.parent.postMessage({
            type: 'verso/executeCell',
            payload: { cellId: cellId }
        }, '*');
    }

    function log(level, message) {
        ensureBound();
        var lvl = typeof level === 'string' && level.length > 0 ? level : 'info';
        var msg = message == null ? '' : String(message);
        window.parent.postMessage({
            type: 'verso/log',
            payload: { level: lvl, message: msg }
        }, '*');
    }

    window.versoBridgeInit = function (identity) {
        if (bound) return;
        if (!identity || !identity.extensionId || !identity.layoutId || !identity.frameInstanceId) {
            throw new Error('versoBridgeInit requires extensionId, layoutId, and frameInstanceId.');
        }
        bound = {
            extensionId: identity.extensionId,
            layoutId: identity.layoutId,
            frameInstanceId: identity.frameInstanceId
        };
        window.verso = {
            ready: ready,
            send: send,
            onMessage: onMessage,
            interact: interact,
            cellInteract: cellInteract,
            executeCell: executeCell,
            log: log
        };
    };
})();
