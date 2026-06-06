// Dashboard layout drag/resize interop. Registers a post-mount hook with the
// custom-layout host so that whenever DashboardLayout.RenderLayoutAsync emits
// .verso-dashboard-grid into the layout root, this script attaches drag and
// resize listeners to the per-cell slot wrappers. Mouse interactions complete
// by posting layout/interact (interactionType="updateCellPosition") so the
// host-side ILayoutInteractionHandler updates the grid positions.

(function () {
    var EXTENSION_ID = 'verso.layout.dashboard';
    var LAYOUT_ID = 'dashboard';

    function getGridMetrics(grid) {
        if (!grid) return null;
        var style = window.getComputedStyle(grid);
        var gap = parseFloat(style.gap) || 8;
        var paddingLeft = parseFloat(style.paddingLeft) || 24;
        var paddingTop = parseFloat(style.paddingTop) || 16;
        var colWidth = (grid.clientWidth - paddingLeft * 2 + gap) / 12;
        var rowHeight = 50; // matches grid-auto-rows in the layout's inline style
        return { grid: grid, gap: gap, paddingLeft: paddingLeft, paddingTop: paddingTop, colWidth: colWidth, rowHeight: rowHeight };
    }

    function dispatchUpdate(cellId, row, col, width, height) {
        if (!window.versoLayoutInteract || typeof window.versoLayoutInteract.layoutInteract !== 'function') return;
        var payload = JSON.stringify({
            cellId: cellId,
            row: row,
            col: col,
            width: width,
            height: height
        });
        window.versoLayoutInteract
            .layoutInteract(EXTENSION_ID, LAYOUT_ID, null, 'updateCellPosition', payload, cellId)
            .catch(function (err) { console.error('dashboard updateCellPosition failed:', err); });
    }

    function parseSpan(value, fallback) {
        if (!value) return fallback;
        var match = String(value).match(/span\s+(\d+)/);
        return match ? parseInt(match[1], 10) : fallback;
    }

    function attachResize(cellEl) {
        var handle = cellEl.querySelector('.verso-dashboard-resize-handle');
        if (!handle || handle.dataset.versoResizeInit === 'true') return;
        handle.dataset.versoResizeInit = 'true';

        handle.addEventListener('mousedown', function (e) {
            e.preventDefault();
            e.stopPropagation();

            var m = getGridMetrics(cellEl.closest('.verso-dashboard-grid'));
            if (!m) return;

            var startRect = cellEl.getBoundingClientRect();
            var startX = e.clientX;
            var startY = e.clientY;
            var startWidth = startRect.width;
            var startHeight = startRect.height;

            var ghost = document.createElement('div');
            ghost.style.cssText = 'position:fixed;border:2px dashed #0078D4;border-radius:6px;background:rgba(0,120,212,0.05);pointer-events:none;z-index:1000;';
            ghost.style.left = startRect.left + 'px';
            ghost.style.top = startRect.top + 'px';
            ghost.style.width = startWidth + 'px';
            ghost.style.height = startHeight + 'px';
            document.body.appendChild(ghost);

            var label = document.createElement('div');
            label.style.cssText = 'position:fixed;background:#0078D4;color:white;padding:2px 8px;border-radius:3px;font-size:11px;font-family:monospace;pointer-events:none;z-index:1001;';
            document.body.appendChild(label);

            cellEl.classList.add('verso-resizing');

            function onMouseMove(ev) {
                var dx = ev.clientX - startX;
                var dy = ev.clientY - startY;
                var newWidth = Math.max(m.colWidth, startWidth + dx);
                var newHeight = Math.max(m.rowHeight, startHeight + dy);
                var snappedCols = Math.max(1, Math.min(12, Math.round(newWidth / m.colWidth)));
                var snappedRows = Math.max(1, Math.round(newHeight / (m.rowHeight + m.gap)));
                ghost.style.width = (snappedCols * m.colWidth - m.gap) + 'px';
                ghost.style.height = (snappedRows * (m.rowHeight + m.gap) - m.gap) + 'px';
                label.textContent = snappedCols + ' × ' + snappedRows;
                label.style.left = (startRect.left + parseFloat(ghost.style.width) + 8) + 'px';
                label.style.top = (startRect.top + parseFloat(ghost.style.height) - 20) + 'px';
            }

            function onMouseUp(ev) {
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);
                cellEl.classList.remove('verso-resizing');
                ghost.remove();
                label.remove();

                var dx = ev.clientX - startX;
                var dy = ev.clientY - startY;
                var newCols = Math.max(1, Math.min(12, Math.round(Math.max(m.colWidth, startWidth + dx) / m.colWidth)));
                var newRows = Math.max(1, Math.round(Math.max(m.rowHeight, startHeight + dy) / (m.rowHeight + m.gap)));

                var cellId = cellEl.getAttribute('data-cell-slot');
                var colStart = parseInt((cellEl.style.gridColumn.match(/(\d+)/) || [, '1'])[1], 10) - 1;
                var rowStart = parseInt((cellEl.style.gridRow.match(/(\d+)/) || [, '1'])[1], 10) - 1;
                dispatchUpdate(cellId, rowStart, colStart, newCols, newRows);
            }

            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        });
    }

    function attachDrag(cellEl) {
        var dragHandle = cellEl.querySelector('.verso-dashboard-drag-handle');
        if (!dragHandle || dragHandle.dataset.versoDragInit === 'true') return;
        dragHandle.dataset.versoDragInit = 'true';

        dragHandle.addEventListener('mousedown', function (e) {
            if (e.target.closest('button')) return;
            e.preventDefault();
            e.stopPropagation();

            var grid = cellEl.closest('.verso-dashboard-grid');
            var m = getGridMetrics(grid);
            if (!m) return;

            var gridRect = grid.getBoundingClientRect();
            var startRect = cellEl.getBoundingClientRect();
            var startX = e.clientX;
            var startY = e.clientY;
            // Where inside the tile the pointer grabbed, so the drop target tracks the
            // tile's top-left corner (matching the ghost) rather than the cursor itself.
            var grabOffsetX = startX - startRect.left;
            var grabOffsetY = startY - startRect.top;

            var ghost = cellEl.cloneNode(true);
            ghost.style.cssText = 'position:fixed;pointer-events:none;z-index:1000;opacity:0.7;width:' +
                startRect.width + 'px;height:' + startRect.height + 'px;' +
                'border:2px solid #0078D4;border-radius:6px;background:var(--verso-cell-background,#fff);box-shadow:0 8px 24px rgba(0,0,0,0.2);';
            ghost.style.left = startRect.left + 'px';
            ghost.style.top = startRect.top + 'px';
            document.body.appendChild(ghost);

            var placeholder = document.createElement('div');
            placeholder.style.cssText = 'border:2px dashed #0078D4;border-radius:6px;background:rgba(0,120,212,0.08);pointer-events:none;';
            placeholder.style.gridColumn = cellEl.style.gridColumn;
            placeholder.style.gridRow = cellEl.style.gridRow;
            cellEl.classList.add('verso-dragging');
            grid.appendChild(placeholder);

            function onMouseMove(ev) {
                var dx = ev.clientX - startX;
                var dy = ev.clientY - startY;
                ghost.style.left = (startRect.left + dx) + 'px';
                ghost.style.top = (startRect.top + dy) + 'px';

                var cursorX = ev.clientX - grabOffsetX - gridRect.left - m.paddingLeft;
                var cursorY = ev.clientY - grabOffsetY - gridRect.top - m.paddingTop;
                var targetCol = Math.max(0, Math.min(11, Math.floor(cursorX / m.colWidth)));
                var targetRow = Math.max(0, Math.floor(cursorY / (m.rowHeight + m.gap)));

                var colSpan = parseSpan(cellEl.style.gridColumn, 6);
                var rowSpan = parseSpan(cellEl.style.gridRow, 4);
                var clampedCol = Math.min(targetCol, 12 - colSpan);

                placeholder.style.gridColumn = (clampedCol + 1) + ' / span ' + colSpan;
                placeholder.style.gridRow = (targetRow + 1) + ' / span ' + rowSpan;
            }

            function onMouseUp(ev) {
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);
                cellEl.classList.remove('verso-dragging');
                ghost.remove();
                placeholder.remove();

                var cursorX = ev.clientX - grabOffsetX - gridRect.left - m.paddingLeft;
                var cursorY = ev.clientY - grabOffsetY - gridRect.top - m.paddingTop;
                var targetCol = Math.max(0, Math.min(11, Math.floor(cursorX / m.colWidth)));
                var targetRow = Math.max(0, Math.floor(cursorY / (m.rowHeight + m.gap)));

                var colSpan = parseSpan(cellEl.style.gridColumn, 6);
                var rowSpan = parseSpan(cellEl.style.gridRow, 4);
                var clampedCol = Math.min(targetCol, 12 - colSpan);

                var cellId = cellEl.getAttribute('data-cell-slot');
                dispatchUpdate(cellId, targetRow, clampedCol, colSpan, rowSpan);
            }

            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        });
    }

    function attachAll(rootEl) {
        if (!rootEl) return;
        var cells = rootEl.querySelectorAll('.verso-dashboard-cell[data-cell-slot]');
        for (var i = 0; i < cells.length; i++) {
            attachResize(cells[i]);
            attachDrag(cells[i]);
        }
    }

    function register() {
        if (window.versoCustomLayout && typeof window.versoCustomLayout.registerMountHook === 'function') {
            window.versoCustomLayout.registerMountHook(EXTENSION_ID, LAYOUT_ID, attachAll);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', register);
    } else {
        register();
    }

    // Keep the public surface for compatibility with any external callers; legacy
    // initResizable/initDraggable methods are no longer used by Verso itself.
    window.versoDashboard = {
        attach: attachAll,
        registerMountHook: register
    };
})();
