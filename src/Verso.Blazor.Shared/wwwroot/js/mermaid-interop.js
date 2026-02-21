window.versoMermaid = {
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

        const nodes = document.querySelectorAll('.verso-mermaid-container .mermaid:not([data-processed])');
        if (nodes.length > 0) {
            await window._mermaid.run({ nodes: Array.from(nodes) });
        }
    }
};
