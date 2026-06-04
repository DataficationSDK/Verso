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

    // Move every portaled cell currently inside the layout root back into the hidden pool. Unlike
    // unmount(), this scans the live DOM rather than the tracked list, so it reclaims the cells even
    // if the list is stale — which it is right before an innerHTML swap that would otherwise destroy
    // them. The cell wrappers carry data-cell-id and sit directly inside their [data-cell-slot].
    function harvestToPool(rootEl, poolEl) {
        if (!rootEl || !poolEl) return;
        var nodes = rootEl.querySelectorAll('[data-cell-slot] > [data-cell-id]');
        for (var i = 0; i < nodes.length; i++) {
            poolEl.appendChild(nodes[i]);
        }
        rootEl._versoMountedCells = [];
    }

    // Nearest scrollable ancestor, so a layout re-render can capture and restore scroll position and
    // not jump the page. Falls back to the document scroller when nothing closer scrolls.
    function findScrollParent(el) {
        var node = el && el.parentElement;
        while (node) {
            var oy = window.getComputedStyle(node).overflowY;
            if ((oy === 'auto' || oy === 'scroll') && node.scrollHeight > node.clientHeight) return node;
            node = node.parentElement;
        }
        return document.scrollingElement || document.documentElement;
    }

    // Atomically replace the layout chrome and re-portal the live cells in ONE synchronous frame:
    // harvest the portaled cells back to the pool (so the innerHTML swap can't destroy the live
    // <Cell> DOM that Blazor still owns), swap in the new HTML, then move the cells back into their
    // slots — all before the browser paints. Because no paint happens between the swap and the
    // re-portal, the layout never flashes empty. (Binding the HTML through a Blazor MarkupString
    // instead paints the new empty slots first and only re-portals on the next OnAfterRender, which
    // is what made fold/insert/reorder flash.) Scroll position is preserved across the swap.
    function setLayoutHtml(rootEl, poolEl, html) {
        if (!rootEl) return 0;
        var scroller = findScrollParent(rootEl);
        var prevTop = scroller ? scroller.scrollTop : 0;
        harvestToPool(rootEl, poolEl);
        rootEl.innerHTML = html || '';
        var count = mount(rootEl, poolEl);
        if (scroller) scroller.scrollTop = prevTop;
        return count;
    }

    // After portaling cells into visible slots: re-run mermaid (it renders at zero width inside
    // the display:none pool) and re-lay-out the Monaco editors (created hidden, they come up
    // unmeasured). Deferred a frame so the slots are laid out before Monaco measures them.
    function afterPortal(nodes) {
        if (!nodes || nodes.length === 0) return;
        if (window.versoMermaid && typeof window.versoMermaid.renderAll === 'function') {
            try { window.versoMermaid.renderAll(); } catch (e) { /* mermaid may not be loaded */ }
        }
        if (window.versoMonaco && typeof window.versoMonaco.relayout === 'function') {
            requestAnimationFrame(function () {
                for (var m = 0; m < nodes.length; m++) {
                    var eds = nodes[m].querySelectorAll('.verso-monaco-editor');
                    for (var e = 0; e < eds.length; e++) {
                        if (eds[e].id) {
                            try { window.versoMonaco.relayout(eds[e].id); } catch (err) { /* editor gone */ }
                        }
                    }
                }
            });
        }
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
        afterPortal(mounted);
        return mounted.length;
    }

    // Fill only slots that are still empty by moving their cell out of the pool. Unlike mount(),
    // this leaves already-portaled cells untouched (so it never steals focus or tears down an
    // editor mid-edit). Used after a structural change whose pool update and layout re-render
    // arrive separately: the new cell's slot exists but the cell only just appeared in the pool.
    function portalPending(rootEl, poolEl) {
        if (!rootEl || !poolEl) return 0;
        var slots = rootEl.querySelectorAll('[data-cell-slot]');
        var added = [];
        for (var i = 0; i < slots.length; i++) {
            var slot = slots[i];
            if (slot.querySelector('[data-cell-id]')) continue; // already holds its cell
            var cellNode = findCellNode(poolEl, getCellId(slot));
            if (cellNode) {
                slot.appendChild(cellNode);
                added.push(cellNode);
            }
        }
        if (added.length) {
            rootEl._versoMountedCells = (rootEl._versoMountedCells || []).concat(added);
            afterPortal(added);
        }
        return added.length;
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
        setLayoutHtml: setLayoutHtml,
        unmountSlots: unmount,
        portalPending: portalPending,
        mountCellInSlot: mountCellInSlot,
        updateSlotPosition: updateSlotPosition,
        registerMountHook: registerMountHook,
        notifyMounted: notifyMounted
    };
})();
