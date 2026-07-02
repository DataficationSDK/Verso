/**
 * Cell interaction bridge for Verso notebooks.
 * Routes cell/interact requests to either:
 *   - VS Code extension host via vscodeBridge.sendRequest (WASM)
 *   - Blazor Server via a registered DotNetObjectReference
 */
window.versoCellInteract = (() => {
    let _dotNetRef = null;

    return {
        /**
         * Register a DotNetObjectReference for Blazor Server interop.
         * Not needed for WASM — the vscodeBridge path is used instead.
         */
        register(dotNetRef) {
            _dotNetRef = dotNetRef;
        },

        /**
         * Send a cell interaction request to the host.
         * @param {string} cellId - GUID of the cell
         * @param {string} extensionId - Extension that handles the interaction
         * @param {string} interactionType - Application-defined interaction type
         * @param {string} payloadJson - JSON payload string
         * @param {string|null} outputBlockId - Optional output block to update in place
         * @returns {Promise<string|null>} Response HTML or null
         */
        async cellInteract(cellId, extensionId, interactionType, payloadJson, outputBlockId) {
            if (window.vscodeBridge && typeof window.vscodeBridge.sendRequest === 'function') {
                // vscodeBridge.sendRequest expects a JSON string and returns a JSON string
                var paramsJson = JSON.stringify({
                    cellId,
                    extensionId,
                    interactionType,
                    payload: payloadJson,
                    outputBlockId: outputBlockId || null
                });
                var resultJson = await window.vscodeBridge.sendRequest('cell/interact', paramsJson);
                if (!resultJson) return null;
                try {
                    var result = JSON.parse(resultJson);
                    return result.response || result.Response || null;
                } catch (e) {
                    return resultJson;
                }
            }

            if (_dotNetRef) {
                return await _dotNetRef.invokeMethodAsync(
                    'OnCellInteract', cellId, extensionId, interactionType, payloadJson, outputBlockId || null);
            }

            throw new Error('No cell interaction host available.');
        }
    };
})();

/**
 * Click-through guard for rendered cell previews.
 *
 * A rendered (collapsed-input) cell shows its output as a preview, and a single click on that
 * preview selects the cell, which swaps in the editor. That is the right gesture for plain
 * content, but it also fires when the output contains interactive controls (a button, an input,
 * a link, a web component), so clicking a widget would yank the cell into edit mode instead of
 * letting the widget respond.
 *
 * This installs one listener that, when a click lands on an interactive element inside a
 * clickable preview, stops the event before it reaches the notebook's delegated click handler.
 * The control still receives the click in the target phase, so it works normally; the cell just
 * stays rendered. Clicking any non-interactive part of the preview still enters edit mode.
 *
 * The walk uses event.composedPath() rather than event.target so it sees through Shadow DOM:
 * clicks inside a web component's shadow root are retargeted to the host element by the time they
 * reach document, so target.closest(...) would miss the real control. The composed path still
 * lists the inner nodes (for composed events like click), and a custom element or shadow host is
 * itself treated as interactive so closed shadow roots are covered too.
 *
 * The listener sits on document.body, strictly below the document where the framework registers
 * its delegated click handler, so it always runs first regardless of load order. Authors can mark
 * a custom interactive region with data-verso-interactive to opt it into the same pass-through.
 */
(function installCellPreviewClickGuard() {
    var PREVIEW_CLASS = 'verso-cell-outputs--clickable';
    var INTERACTIVE_SELECTOR =
        'a, button, input, select, textarea, label, summary, option, ' +
        '[contenteditable=""], [contenteditable="true"], ' +
        '[role="button"], [role="link"], [role="checkbox"], [role="tab"], ' +
        '[data-verso-interactive]';

    function isInteractive(el) {
        // A standard control, an ARIA control, or an author opt-in.
        if (el.matches && el.matches(INTERACTIVE_SELECTOR)) return true;
        // A custom element (its tag name carries a hyphen) — the whole web component owns its
        // own interactions, so a click anywhere inside it should not drop into editing.
        if (el.localName && el.localName.indexOf('-') !== -1) return true;
        // A shadow host, even when the shadow root is closed and its internals are hidden.
        if (el.shadowRoot) return true;
        return false;
    }

    function onClick(e) {
        var path = (e.composedPath && e.composedPath()) || null;
        if (!path || !path.length) return;

        var sawInteractive = false;
        for (var i = 0; i < path.length; i++) {
            var node = path[i];
            if (!node || node.nodeType !== 1) continue;        // elements only
            if (node.classList && node.classList.contains(PREVIEW_CLASS)) {
                // Reached the edit-trigger surface. If anything below it was interactive, let the
                // control keep the click and leave the cell rendered.
                if (sawInteractive) e.stopPropagation();
                return;
            }
            if (isInteractive(node)) sawInteractive = true;
        }
    }

    function install() {
        if (!document.body || document.body.dataset.versoCellPreviewGuard === '1') return;
        document.body.dataset.versoCellPreviewGuard = '1';
        document.body.addEventListener('click', onClick);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', install);
    } else {
        install();
    }
})();
