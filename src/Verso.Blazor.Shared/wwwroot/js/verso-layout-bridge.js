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

    window.addEventListener('message', function (event) {
        var data = event.data;
        if (!data || typeof data !== 'object' || typeof data.type !== 'string') return;
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

    function ready() {
        ensureBound();
        window.parent.postMessage({
            type: 'verso/ready',
            payload: {
                extensionId: bound.extensionId,
                layoutId: bound.layoutId,
                frameInstanceId: bound.frameInstanceId
            }
        }, '*');
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
