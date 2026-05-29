/**
 * Inline-layout bridge for author-supplied scripts.
 *
 * Exposes `window.verso.layout` so a layout's script asset can post typed
 * interactions to its ILayoutInteractionHandler, request a re-render, and read
 * theme tokens without each layout reinventing the channel.
 *
 * The iframe-isolation path (verso-layout-bridge.js) sets `window.verso` inside
 * the iframe document, so there is no collision on the host page; this file is
 * the inline-host counterpart and is the only thing that touches `window.verso`
 * on the host page.
 *
 * Interaction transport is delegated to window.versoLayoutInteract from
 * layout-interact-interop.js, which already routes to either the VS Code
 * extension host or a registered DotNetObjectReference depending on the host.
 */
(function () {
    if (window.verso && window.verso.layout) return;
    window.verso = window.verso || {};

    function ensureTransport() {
        if (!window.versoLayoutInteract || typeof window.versoLayoutInteract.layoutInteract !== 'function') {
            throw new Error('verso.layout: window.versoLayoutInteract is not available (layout-interact-interop.js must load before verso-inline-layout-bridge.js)');
        }
        return window.versoLayoutInteract;
    }

    function makeBridge(extensionId, layoutId, frameInstanceId) {
        return {
            extensionId,
            layoutId,
            frameInstanceId,

            /**
             * Send a typed interaction to the layout's ILayoutInteractionHandler.
             * @param {string} interactionType
             * @param {*} [payload] String passed verbatim; anything else is JSON.stringified.
             * @param {string|null} [targetId]
             * @returns {Promise<*>}
             */
            interact(interactionType, payload, targetId) {
                const t = ensureTransport();
                const payloadJson = typeof payload === 'string'
                    ? payload
                    : JSON.stringify(payload === undefined ? null : payload);
                return t.layoutInteract(
                    extensionId, layoutId, frameInstanceId,
                    interactionType, payloadJson, targetId || null);
            },

            /**
             * Ask the host to re-render the layout. Implemented as a reserved
             * interactionType the host intercepts; no handler implementation needed.
             */
            requestRender() {
                return this.interact('verso/requestRender', '');
            },

            /**
             * Read a theme token by short name (e.g. "bg-default", "fg-default").
             * Resolves the corresponding --verso-<name> CSS custom property.
             * Returns the empty string if the property is not set.
             */
            getThemeToken(name) {
                return getComputedStyle(document.documentElement)
                    .getPropertyValue('--verso-' + name).trim();
            },

            /**
             * Subscribe to host theme-change notifications.
             * The handler receives the same { kind, tokens } shape the host writes.
             * Returns an unsubscribe function.
             */
            onThemeChange(handler) {
                if (typeof handler !== 'function') return function () { };
                const wrapped = function (e) { handler(e.detail); };
                window.addEventListener('verso:themechanged', wrapped);
                return function () { window.removeEventListener('verso:themechanged', wrapped); };
            },
        };
    }

    /**
     * Theme-change dispatch surface. Called by the Razor ThemeProvider when the
     * active theme changes so layout scripts subscribed via bridge.onThemeChange()
     * receive a structured payload they can act on without polling computed style.
     */
    window.verso.theme = window.verso.theme || {
        dispatchChange(kind, tokens) {
            window.dispatchEvent(new CustomEvent('verso:themechanged', {
                detail: { kind: kind || null, tokens: tokens || {} }
            }));
        },
    };

    window.verso.layout = {
        /**
         * Bind the bridge using identifiers from this script's data-* attributes.
         * Only callable from classic scripts; ES modules see a null
         * document.currentScript and must call bind({...}) directly.
         */
        bindCurrentScript() {
            const el = document.currentScript;
            if (!el) {
                throw new Error('verso.layout.bindCurrentScript: document.currentScript is null. ES modules must call window.verso.layout.bind({extensionId, layoutId, frameInstanceId}) directly.');
            }
            const ds = el.dataset || {};
            const extId = ds.versoExtensionId;
            const layoutId = ds.versoLayoutId;
            const frameId = ds.versoFrameInstanceId;
            if (!extId || !layoutId) {
                throw new Error('verso.layout.bindCurrentScript: script tag is missing data-verso-extension-id or data-verso-layout-id.');
            }
            return makeBridge(extId, layoutId, frameId || '');
        },

        /**
         * Bind the bridge with explicit identifiers. Use this from ES modules or
         * any context where document.currentScript is not available.
         */
        bind(opts) {
            opts = opts || {};
            if (!opts.extensionId || !opts.layoutId) {
                throw new Error('verso.layout.bind: { extensionId, layoutId } are required.');
            }
            return makeBridge(opts.extensionId, opts.layoutId, opts.frameInstanceId || '');
        },
    };
})();
