window.versoMermaid = {
    _pending: null,

    renderAll: async function () {
        if (!window._mermaid) {
            // One shared promise keeps concurrent renders from injecting the
            // tag twice.
            if (!window._mermaidLoading) {
                window._mermaidLoading = this._load().then(function (mermaid) {
                    mermaid.initialize({ startOnLoad: false, theme: 'default' });
                    return mermaid;
                });
            }
            try {
                window._mermaid = await window._mermaidLoading;
            } catch (e) {
                console.error('Failed to load mermaid.js:', e);
                window._mermaidLoading = null;
                return;
            }
        }

        // Two kinds of node match: the pre a dedicated mermaid cell renders inside
        // its container, and the div Markdig's diagram extension writes for a
        // ```mermaid fence in a markdown cell. Both sit under a verso-output--html
        // wrapper.
        //
        // Mermaid measures text in place with getBBox(), so rendering a node while an
        // ancestor is display:none (the portal pool, a prerendered presenter overlay)
        // collapses the diagram, and data-processed makes that permanent. Render only
        // nodes that currently have a layout box; park the rest until they get one.
        const nodes = document.querySelectorAll('.verso-output--html .mermaid:not([data-processed])');
        const ready = [];
        for (const node of nodes) {
            if (!window.ResizeObserver || this._hasBox(node)) ready.push(node);
            else this._renderWhenVisible(node);
        }
        if (ready.length > 0) {
            await window._mermaid.run({ nodes: ready });
        }
    },

    // Fetched and evaluated by hand, on purpose. Monaco's AMD loader owns the
    // global define, and a dependency inside mermaid's bundles registers with
    // any function-typed define it sees, no amd flag required, which fails the
    // whole load ("Can only have one anonymous define call per script file");
    // with an ESM import that also poisons the module cache for the rest of
    // the page. No script tag or import can dodge that, so the source runs in
    // a function whose define parameter shadows the loader for the entire
    // evaluation. The IIFE build is the one that expects to run as a classic
    // script and land on window.mermaid.
    //
    // The prologue pre-links esbuild's namespace var to globalThis: a var in
    // a function body is not a global, and the bundle's last line reads the
    // name off globalThis. If a future build renames it, the mermaid check
    // below turns that into a loud failure instead of a quiet one.
    _load: async function () {
        const response = await fetch('https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js');
        if (!response.ok) {
            throw new Error('mermaid.min.js failed to load: HTTP ' + response.status);
        }
        const source = await response.text();
        new Function('define', 'exports', 'module',
            '"use strict";var __esbuild_esm_mermaid_nm=(globalThis.__esbuild_esm_mermaid_nm=globalThis.__esbuild_esm_mermaid_nm||{});\n'
            + source + '\n//# sourceURL=verso-mermaid-cdn.js')();
        if (!window.mermaid) {
            throw new Error('mermaid.min.js did not install window.mermaid');
        }
        return window.mermaid;
    },

    _hasBox: function (node) {
        return node.checkVisibility ? node.checkVisibility() : node.offsetParent !== null;
    },

    // A ResizeObserver fires when an observed element gains a box, which is exactly
    // the moment a hidden node becomes renderable: its slide turns current, a pane
    // unfolds, the pool cell is portaled into a visible slot.
    _renderWhenVisible: function (node) {
        if (!this._pending) {
            const self = this;
            this._pending = new ResizeObserver(function (entries) {
                const ready = [];
                for (const entry of entries) {
                    const el = entry.target;
                    if (!el.isConnected || el.hasAttribute('data-processed')) {
                        self._pending.unobserve(el);
                        continue;
                    }
                    if (entry.contentRect.width > 0 && self._hasBox(el)) {
                        self._pending.unobserve(el);
                        ready.push(el);
                    }
                }
                if (ready.length > 0 && window._mermaid) {
                    window._mermaid.run({ nodes: ready }).catch(function (e) {
                        console.error('Mermaid render failed:', e);
                    });
                }
            });
        }
        this._pending.observe(node);
    }
};
