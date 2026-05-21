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

    window.versoCustomLayout = {
        mountSlots: function (rootEl, poolEl) {
            unmount(rootEl, poolEl);
            return mount(rootEl, poolEl);
        },
        unmountSlots: unmount,
        mountCellInSlot: mountCellInSlot,
        updateSlotPosition: updateSlotPosition
    };
})();
