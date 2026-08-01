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
 * Top-layer positioning for the per-cell type and language dropdown menus.
 *
 * Every code cell hosts a Monaco editor whose internals are promoted to their own GPU compositing
 * layers (Monaco sets transform + contain:strict on its content, scrollbars, and margin). A menu
 * painted on the normal layer, even at a very high z-index, can be composited *beneath* a
 * neighbouring cell's editor layer, so the next cell's chrome bleeds through the open menu. This
 * is a compositor-overlap effect: DOM hit-testing still reports the menu on top, but the GPU
 * paints the neighbour over it, and no stacking-context or z-index change fixes it.
 *
 * Rendering the menu in the top layer (via the Popover API) sidesteps compositing entirely, since
 * top-layer content always paints above every other layer on the page. The menu keeps its own
 * open/close state in the component; this helper only shows the popover and places it at its
 * trigger badge, flipping above the badge when it would overflow the viewport and clamping so it
 * stays on screen. While open it tracks the badge on scroll and resize; the tracking listeners
 * remove themselves once the popover closes or its element leaves the DOM.
 *
 * Despite the name, nothing here is specific to cells: it positions any menu at any trigger. The
 * toolbar's export menu uses it for a different reason, that the toolbar clips its own overflow.
 */
window.versoCellMenu = (function () {
    // Active scroll/resize trackers keyed by a stable per-menu id, so close(key) can tear a menu's
    // tracker down even after the component removed the popover element from the DOM (which is what
    // closes the popover). Keying by id, rather than stashing the cleanup on the element, means the
    // teardown never depends on still holding a live reference to a node that may already be gone.
    var trackers = {};

    // ':popover-open' is an invalid selector on engines without the Popover API, where matches()
    // throws; treat those as "not open" instead of letting the throw escape a scroll handler.
    function isOpen(el) {
        try { return !!(el && el.matches && el.matches(':popover-open')); }
        catch (e) { return false; }
    }

    function position(popupEl, anchorEl) {
        var a = anchorEl.getBoundingClientRect();
        popupEl.style.position = 'fixed';
        popupEl.style.margin = '0';
        // Provisional placement so offsetWidth/Height reflect the real menu box before we flip.
        popupEl.style.top = '0px';
        popupEl.style.left = '0px';
        var pw = popupEl.offsetWidth;
        var ph = popupEl.offsetHeight;
        var vw = window.innerWidth;
        var vh = window.innerHeight;
        var gap = 2;
        var top = a.bottom + gap;
        // Flip above the badge when the menu would run past the viewport bottom and there is room.
        if (top + ph > vh - 4 && a.top - gap - ph >= 4) top = a.top - gap - ph;
        // Final clamp so the whole box stays on-screen even when it fits neither fully below nor
        // fully above the badge (a short editor pane). The menu's own max-height keeps ph bounded,
        // so this never pins a taller-than-viewport box; its internal overflow-y handles the rest.
        top = Math.max(4, Math.min(top, vh - 4 - ph));
        var left = a.left;
        // Keep the menu within the viewport horizontally (paired with the CSS max-width cap).
        if (left + pw > vw - 4) left = vw - 4 - pw;
        if (left < 4) left = 4;
        popupEl.style.top = Math.round(top) + 'px';
        popupEl.style.left = Math.round(left) + 'px';
    }

    function open(popupEl, anchorEl, key) {
        if (!popupEl || !anchorEl) return;
        try {
            if (popupEl.showPopover && !isOpen(popupEl)) popupEl.showPopover();
        } catch (e) { /* popover unsupported or already open: the element keeps its own CSS */ }
        position(popupEl, anchorEl);
        if (trackers[key]) trackers[key]();   // drop a stale tracker for this menu
        var track = function () {
            // Self-heal: once the menu has closed or its element is gone, stop tracking.
            if (!popupEl.isConnected || !isOpen(popupEl)) { cleanup(); return; }
            position(popupEl, anchorEl);
        };
        var cleanup = function () {
            window.removeEventListener('scroll', track, true);
            window.removeEventListener('resize', track);
            if (trackers[key] === cleanup) delete trackers[key];
        };
        // Capture-phase scroll so movement in any scroll container (not just the window) tracks.
        window.addEventListener('scroll', track, true);
        window.addEventListener('resize', track);
        trackers[key] = cleanup;
    }

    // Called on the close transition (the popover element is already gone, which hid the popover);
    // this only needs to remove the tracker's window listeners for that menu.
    function close(key) {
        if (trackers[key]) trackers[key]();
    }

    return { open: open, close: close };
})();

/**
 * Click-through guard for rendered cell previews.
 *
 * A rendered (collapsed-input) cell shows its output as a preview, and a single click on that
 * preview selects the cell, which swaps in the editor. That is the right gesture for plain
 * content, but two kinds of click must not trigger it:
 *
 *   - A click on an interactive control inside the output (a button, an input, a link, a web
 *     component). The widget should respond; the cell should stay rendered.
 *   - The click that ends a drag-selection of preview text. Selecting the cell unmounts the
 *     preview DOM to make room for the editor, which destroys the highlight before it can be
 *     copied. A plain click is unaffected because the browser collapses any selection on
 *     mousedown, so by the time its click event fires there is no live selection; only a drag
 *     (or a shift-click extension) reaches the click event with one.
 *
 * This installs one listener that stops such clicks before they reach the notebook's delegated
 * click handler. For the interactive case the control still receives the click in the target
 * phase, so it works normally. Clicking any non-interactive part of the preview without a live
 * selection still enters edit mode.
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

    function hasSelectionWithin(previewEl) {
        var sel = window.getSelection ? window.getSelection() : null;
        if (!sel || sel.isCollapsed || sel.rangeCount === 0) return false;
        // Only honor a selection that actually involves this preview. A leftover selection
        // elsewhere on the page (mousedown normally clears one, but not in every engine and
        // not for shift-clicks) should not block editing this cell.
        for (var i = 0; i < sel.rangeCount; i++) {
            var range = sel.getRangeAt(i);
            if (range.intersectsNode && range.intersectsNode(previewEl)) return true;
        }
        return false;
    }

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
                // Reached the edit-trigger surface. If anything below it was interactive, or the
                // click is the tail of a drag that selected text in this preview, leave the cell
                // rendered.
                if (sawInteractive || hasSelectionWithin(node)) e.stopPropagation();
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
