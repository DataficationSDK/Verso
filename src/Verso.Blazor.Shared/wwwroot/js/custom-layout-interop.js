// Custom layout portal interop. Physically moves cell DOM nodes from the page-level
// hidden cell pool into [data-cell-slot] slots inside the extension-rendered layout HTML.
// Cells remain owned by Blazor — only their DOM location changes — so re-rendering the
// layout does not tear down editor state, output state, etc.

(function () {
    function getCellId(slotEl) {
        return slotEl.getAttribute('data-cell-slot');
    }

    function findCellNode(poolEl, cellId) {
        if (!poolEl || !cellId) return null;
        return poolEl.querySelector('[data-cell-id="' + CSS.escape(cellId) + '"]');
    }

    function unmount(rootEl, poolEl) {
        if (!rootEl || !poolEl) return;
        var mounted = rootEl._versoMountedCells;
        if (!mounted || mounted.length === 0) return;
        for (var i = 0; i < mounted.length; i++) {
            var node = mounted[i];
            if (node && node.parentNode) {
                poolEl.appendChild(node);
            }
        }
        rootEl._versoMountedCells = [];
    }

    function mount(rootEl, poolEl) {
        if (!rootEl || !poolEl) return [];
        var slots = rootEl.querySelectorAll('[data-cell-slot]');
        var mounted = [];
        for (var i = 0; i < slots.length; i++) {
            var slot = slots[i];
            var cellId = getCellId(slot);
            var cellNode = findCellNode(poolEl, cellId);
            if (cellNode) {
                slot.appendChild(cellNode);
                mounted.push(cellNode);
            }
        }
        rootEl._versoMountedCells = mounted;

        // Mermaid diagrams in the cell pool render at zero width because the pool is
        // display:none until the cell is portaled. Re-run renderAll now that the
        // diagrams are in a sized container so they layout correctly.
        if (mounted.length > 0 && window.versoMermaid && typeof window.versoMermaid.renderAll === 'function') {
            try { window.versoMermaid.renderAll(); } catch (e) { /* mermaid may not be loaded */ }
        }

        return mounted.length;
    }

    function mountCellInSlot(cellId, slotEl, poolEl) {
        if (!slotEl) return false;
        var cellNode = findCellNode(poolEl, cellId);
        if (!cellNode) return false;
        slotEl.appendChild(cellNode);
        return true;
    }

    function updateSlotPosition(rootEl, cellId, row, col, width, height) {
        if (!rootEl || !cellId) return false;
        var slot = rootEl.querySelector('[data-cell-slot="' + CSS.escape(cellId) + '"]');
        if (!slot) return false;
        slot.setAttribute('data-row', String(row));
        slot.setAttribute('data-col', String(col));
        slot.setAttribute('data-width', String(width));
        slot.setAttribute('data-height', String(height));
        slot.style.gridArea =
            (row + 1) + ' / ' + (col + 1) + ' / ' +
            (row + 1 + height) + ' / ' + (col + 1 + width);
        return true;
    }

    // Layout-specific post-mount hooks. Modules like dashboard-interop.js register an
    // entry keyed by `${extensionId}:${layoutId}`; CustomLayoutHtml calls notifyMounted
    // after the layout HTML is in the DOM. Hooks attach drag/resize handlers, refresh
    // visualizations, etc., without the framework knowing about them.
    var mountHooks = {};

    function registerMountHook(extensionId, layoutId, fn) {
        if (typeof fn !== 'function') return;
        mountHooks[extensionId + ':' + layoutId] = fn;
    }

    function notifyMounted(rootEl, extensionId, layoutId) {
        if (!rootEl) return;
        var hook = mountHooks[extensionId + ':' + layoutId];
        if (hook) {
            try { hook(rootEl); } catch (e) { console.warn('Verso layout mount hook failed:', e); }
        }
    }

    window.versoCustomLayout = {
        mountSlots: function (rootEl, poolEl) {
            unmount(rootEl, poolEl);
            return mount(rootEl, poolEl);
        },
        unmountSlots: unmount,
        mountCellInSlot: mountCellInSlot,
        updateSlotPosition: updateSlotPosition,
        registerMountHook: registerMountHook,
        notifyMounted: notifyMounted
    };
})();
