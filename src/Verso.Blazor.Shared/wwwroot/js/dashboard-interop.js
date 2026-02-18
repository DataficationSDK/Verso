// Dashboard drag/resize interop for Blazor
window.versoDashboard = {

    _getGridMetrics: function (el) {
        const grid = el.closest('.verso-dashboard-grid');
        if (!grid) return null;
        const style = window.getComputedStyle(grid);
        const gap = parseFloat(style.gap) || 8;
        const paddingLeft = parseFloat(style.paddingLeft) || 24;
        const paddingTop = parseFloat(style.paddingTop) || 16;
        const colWidth = (grid.clientWidth - paddingLeft * 2 + gap) / 12;
        const rowHeight = 50; // matches grid-auto-rows in CSS
        return { grid, gap, paddingLeft, paddingTop, colWidth, rowHeight };
    },

    dispose: function (elementId) {
        const el = document.getElementById(elementId);
        if (el) {
            delete el.dataset.resizeInit;
            delete el.dataset.dragInit;
        }
    },

    initResizable: function (elementId, dotnetRef) {
        const el = document.getElementById(elementId);
        if (!el) return;

        // Skip if this exact DOM node is already initialized
        if (el.dataset.resizeInit) return;

        const handle = el.querySelector('.verso-dashboard-resize-handle');
        if (!handle) return;

        el.dataset.resizeInit = 'true';
        const self = this;

        handle.addEventListener('mousedown', function (e) {
            e.preventDefault();
            e.stopPropagation();

            const m = self._getGridMetrics(el);
            if (!m) return;

            const startRect = el.getBoundingClientRect();
            const startX = e.clientX;
            const startY = e.clientY;
            const startWidth = startRect.width;
            const startHeight = startRect.height;

            const ghost = document.createElement('div');
            ghost.style.cssText = 'position:fixed;border:2px dashed #0078D4;border-radius:6px;background:rgba(0,120,212,0.05);pointer-events:none;z-index:1000;';
            ghost.style.left = startRect.left + 'px';
            ghost.style.top = startRect.top + 'px';
            ghost.style.width = startWidth + 'px';
            ghost.style.height = startHeight + 'px';
            document.body.appendChild(ghost);

            const label = document.createElement('div');
            label.style.cssText = 'position:fixed;background:#0078D4;color:white;padding:2px 8px;border-radius:3px;font-size:11px;font-family:monospace;pointer-events:none;z-index:1001;';
            document.body.appendChild(label);

            el.classList.add('verso-resizing');

            function onMouseMove(e) {
                const dx = e.clientX - startX;
                const dy = e.clientY - startY;
                const newWidth = Math.max(m.colWidth, startWidth + dx);
                const newHeight = Math.max(m.rowHeight, startHeight + dy);
                const snappedCols = Math.max(1, Math.min(12, Math.round(newWidth / m.colWidth)));
                const snappedRows = Math.max(1, Math.round(newHeight / (m.rowHeight + m.gap)));
                ghost.style.width = (snappedCols * m.colWidth - m.gap) + 'px';
                ghost.style.height = (snappedRows * (m.rowHeight + m.gap) - m.gap) + 'px';
                label.textContent = snappedCols + ' \u00d7 ' + snappedRows;
                label.style.left = (startRect.left + parseFloat(ghost.style.width) + 8) + 'px';
                label.style.top = (startRect.top + parseFloat(ghost.style.height) - 20) + 'px';
            }

            function onMouseUp(e) {
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);
                el.classList.remove('verso-resizing');
                ghost.remove();
                label.remove();

                const dx = e.clientX - startX;
                const dy = e.clientY - startY;
                const newCols = Math.max(1, Math.min(12, Math.round(Math.max(m.colWidth, startWidth + dx) / m.colWidth)));
                const newRows = Math.max(1, Math.round(Math.max(m.rowHeight, startHeight + dy) / (m.rowHeight + m.gap)));
                if (dotnetRef) {
                    dotnetRef.invokeMethodAsync('OnResizeComplete', newCols, newRows);
                }
            }

            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        });
    },

    initDraggable: function (elementId, dotnetRef) {
        const el = document.getElementById(elementId);
        if (!el) return;

        // Skip if this exact DOM node is already initialized
        if (el.dataset.dragInit) return;

        const dragHandle = el.querySelector('.verso-dashboard-drag-handle');
        if (!dragHandle) return;

        el.dataset.dragInit = 'true';
        const self = this;

        dragHandle.addEventListener('mousedown', function (e) {
            // Don't drag if clicking a button inside the handle
            if (e.target.closest('button')) return;
            e.preventDefault();
            e.stopPropagation();

            const m = self._getGridMetrics(el);
            if (!m) return;

            const gridRect = m.grid.getBoundingClientRect();
            const startRect = el.getBoundingClientRect();
            const startX = e.clientX;
            const startY = e.clientY;

            // Create a ghost clone that follows the cursor
            const ghost = el.cloneNode(true);
            ghost.style.cssText = 'position:fixed;pointer-events:none;z-index:1000;opacity:0.7;width:' +
                startRect.width + 'px;height:' + startRect.height + 'px;' +
                'border:2px solid #0078D4;border-radius:6px;background:var(--verso-cell-background,#fff);box-shadow:0 8px 24px rgba(0,0,0,0.2);';
            ghost.style.left = startRect.left + 'px';
            ghost.style.top = startRect.top + 'px';
            document.body.appendChild(ghost);

            // Create a drop placeholder in the grid
            const placeholder = document.createElement('div');
            placeholder.style.cssText = 'border:2px dashed #0078D4;border-radius:6px;background:rgba(0,120,212,0.08);pointer-events:none;';
            // Copy the cell's grid placement to the placeholder
            placeholder.style.gridColumn = el.style.gridColumn;
            placeholder.style.gridRow = el.style.gridRow;

            el.classList.add('verso-dragging');
            m.grid.appendChild(placeholder);

            function onMouseMove(e) {
                const dx = e.clientX - startX;
                const dy = e.clientY - startY;

                // Move ghost with cursor
                ghost.style.left = (startRect.left + dx) + 'px';
                ghost.style.top = (startRect.top + dy) + 'px';

                // Calculate target grid position based on cursor position in grid
                const cursorX = e.clientX - gridRect.left - m.paddingLeft;
                const cursorY = e.clientY - gridRect.top - m.paddingTop;
                const targetCol = Math.max(0, Math.min(11, Math.floor(cursorX / m.colWidth)));
                const targetRow = Math.max(0, Math.floor(cursorY / (m.rowHeight + m.gap)));

                // Get the cell's current span from its style
                const colMatch = el.style.gridColumn.match(/span\s+(\d+)/);
                const rowMatch = el.style.gridRow.match(/span\s+(\d+)/);
                const colSpan = colMatch ? parseInt(colMatch[1]) : 6;
                const rowSpan = rowMatch ? parseInt(rowMatch[1]) : 4;

                // Clamp so cell doesn't go off-grid
                const clampedCol = Math.min(targetCol, 12 - colSpan);
                const clampedRow = targetRow;

                placeholder.style.gridColumn = (clampedCol + 1) + ' / span ' + colSpan;
                placeholder.style.gridRow = (clampedRow + 1) + ' / span ' + rowSpan;
            }

            function onMouseUp(e) {
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);
                el.classList.remove('verso-dragging');
                ghost.remove();
                placeholder.remove();

                // Calculate final drop position
                const cursorX = e.clientX - gridRect.left - m.paddingLeft;
                const cursorY = e.clientY - gridRect.top - m.paddingTop;
                const targetCol = Math.max(0, Math.min(11, Math.floor(cursorX / m.colWidth)));
                const targetRow = Math.max(0, Math.floor(cursorY / (m.rowHeight + m.gap)));

                const colMatch = el.style.gridColumn.match(/span\s+(\d+)/);
                const rowMatch = el.style.gridRow.match(/span\s+(\d+)/);
                const colSpan = colMatch ? parseInt(colMatch[1]) : 6;
                const rowSpan = rowMatch ? parseInt(rowMatch[1]) : 4;

                const clampedCol = Math.min(targetCol, 12 - colSpan);

                if (dotnetRef) {
                    dotnetRef.invokeMethodAsync('OnMoveComplete', clampedCol, targetRow);
                }
            }

            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        });
    }
};
