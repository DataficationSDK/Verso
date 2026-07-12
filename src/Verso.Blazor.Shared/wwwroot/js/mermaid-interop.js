window.versoMermaid = {
    _pending: null,

    renderAll: async function () {
        if (!window._mermaid) {
            try {
                const { default: mermaid } = await import(
                    'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs'
                );
                mermaid.initialize({ startOnLoad: false, theme: 'default' });
                window._mermaid = mermaid;
            } catch (e) {
                console.error('Failed to load mermaid.js:', e);
                return;
            }
        }

        // Mermaid measures text in place with getBBox(), so rendering a node while an
        // ancestor is display:none (the portal pool, a prerendered presenter overlay)
        // collapses the diagram, and data-processed makes that permanent. Render only
        // nodes that currently have a layout box; park the rest until they get one.
        const nodes = document.querySelectorAll('.verso-mermaid-container .mermaid:not([data-processed])');
        const ready = [];
        for (const node of nodes) {
            if (!window.ResizeObserver || this._hasBox(node)) ready.push(node);
            else this._renderWhenVisible(node);
        }
        if (ready.length > 0) {
            await window._mermaid.run({ nodes: ready });
        }
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
