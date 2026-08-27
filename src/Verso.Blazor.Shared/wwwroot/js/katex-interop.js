window.versoKatex = {
    renderAll: async function () {
        // Markdig's math extension wraps inline math in a span and display math in a
        // div, both carrying class="math" with the TeX delimiters still in the text.
        // A cheap existence probe keeps pages without math from ever paying for the
        // library.
        if (!document.querySelector('.verso-output--html .math:not([data-katex-processed])')) {
            return;
        }

        if (!window._katexLoading) {
            window._katexLoading = import(
                'https://cdn.jsdelivr.net/npm/katex@0.18/dist/katex.mjs'
            ).then(function (module) {
                return module['default'];
            });
        }

        let katex;
        try {
            katex = await window._katexLoading;
        } catch (e) {
            console.error('Failed to load KaTeX:', e);
            window._katexLoading = null;
            return;
        }

        if (!document.getElementById('verso-katex-css')) {
            const link = document.createElement('link');
            link.id = 'verso-katex-css';
            link.rel = 'stylesheet';
            link.href = 'https://cdn.jsdelivr.net/npm/katex@0.18/dist/katex.min.css';
            document.head.appendChild(link);
        }

        // Queried only now, after the await. Renders overlapping this one while the
        // library was still on the wire each triggered their own call; an early node
        // list would make every one of them typeset the same elements again, feeding
        // KaTeX its own output. From here to the end there is no await, so one call
        // stamps and renders the lot and the rest find nothing left.
        const nodes = document.querySelectorAll('.verso-output--html .math:not([data-katex-processed])');
        for (const node of nodes) {
            // Stamped before rendering, so a formula that fails is not retried on
            // every subsequent render of the same outputs.
            node.setAttribute('data-katex-processed', 'true');
            const math = this._read(node);
            try {
                katex.render(math.tex, node, {
                    displayMode: math.display,
                    throwOnError: false
                });
            } catch (e) {
                // throwOnError:false still throws on some malformed input. The
                // source text stays in place, which is the most useful failure.
            }
        }
    },

    // The delimiters say which mode the author wrote: \( \) is inline, \[ \] is
    // display. Bare content falls back to the element Markdig chose for it.
    _read: function (node) {
        const text = node.textContent.trim();
        if (text.startsWith('\\(') && text.endsWith('\\)')) {
            return { tex: text.slice(2, -2), display: false };
        }
        if (text.startsWith('\\[') && text.endsWith('\\]')) {
            return { tex: text.slice(2, -2), display: true };
        }
        return { tex: text, display: node.tagName === 'DIV' };
    }
};
