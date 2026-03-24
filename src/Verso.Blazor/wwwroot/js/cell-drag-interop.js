window.versoCellDrag = (() => {
    let _dotnetRef = null;
    let _onMouseDown = null;

    function init(dotnetRef) {
        _dotnetRef = dotnetRef;
        _onMouseDown = handleMouseDown;
        document.addEventListener('mousedown', _onMouseDown);
    }

    function dispose() {
        if (_onMouseDown) {
            document.removeEventListener('mousedown', _onMouseDown);
            _onMouseDown = null;
        }
        _dotnetRef = null;
    }

    function handleMouseDown(e) {
        if (e.button !== 0) return;

        const gutter = e.target.closest('.verso-cell-gutter');
        if (!gutter) return;
        if (e.target.closest('button')) return;

        const wrapper = e.target.closest('[data-cell-index]');
        if (!wrapper) return;
        const fromIndex = parseInt(wrapper.dataset.cellIndex, 10);
        if (isNaN(fromIndex)) return;

        e.preventDefault();
        e.stopPropagation();

        const cellList = document.querySelector('.verso-cell-list');
        if (!cellList) return;

        const cellEl = gutter.closest('.verso-cell');
        if (!cellEl) return;

        const rect = cellEl.getBoundingClientRect();
        const offsetX = e.clientX - rect.left;
        const offsetY = e.clientY - rect.top;

        // Ghost element
        const ghost = document.createElement('div');
        ghost.style.cssText =
            'position:fixed;pointer-events:none;z-index:1000;' +
            'opacity:0.55;width:' + rect.width + 'px;height:' + Math.min(rect.height, 80) + 'px;' +
            'left:' + rect.left + 'px;top:' + rect.top + 'px;' +
            'border:2px solid var(--verso-cell-active-border,#0078D4);' +
            'border-radius:4px;background:var(--verso-cell-background,#1e1e1e);' +
            'box-shadow:0 4px 16px rgba(0,0,0,0.15);';
        document.body.appendChild(ghost);

        // Drop indicator
        const indicator = document.createElement('div');
        indicator.className = 'verso-cell-drop-indicator';
        indicator.style.display = 'none';
        cellList.appendChild(indicator);

        cellEl.classList.add('verso-cell--dragging');
        const prevCursor = document.body.style.cursor;
        document.body.style.cursor = 'grabbing';

        let currentInsertBefore = fromIndex;
        let scrollRafId = null;

        // Auto-scroll when cursor is near the top or bottom edge
        const SCROLL_ZONE = 60; // px from edge to start scrolling
        const SCROLL_SPEED = 8; // px per frame

        function getScrollContainer() {
            // In VS Code webview the scroller may be the document element or body
            let el = cellList.parentElement;
            while (el) {
                const style = getComputedStyle(el);
                if (style.overflowY === 'auto' || style.overflowY === 'scroll') return el;
                el = el.parentElement;
            }
            return document.documentElement;
        }

        const scrollContainer = getScrollContainer();

        function autoScroll(clientY) {
            if (scrollRafId) cancelAnimationFrame(scrollRafId);

            const containerRect = scrollContainer === document.documentElement
                ? { top: 0, bottom: window.innerHeight }
                : scrollContainer.getBoundingClientRect();

            const distFromTop = clientY - containerRect.top;
            const distFromBottom = containerRect.bottom - clientY;

            let scrollDelta = 0;
            if (distFromTop < SCROLL_ZONE) {
                scrollDelta = -SCROLL_SPEED * (1 - distFromTop / SCROLL_ZONE);
            } else if (distFromBottom < SCROLL_ZONE) {
                scrollDelta = SCROLL_SPEED * (1 - distFromBottom / SCROLL_ZONE);
            }

            if (scrollDelta !== 0) {
                scrollContainer.scrollTop += scrollDelta;
                scrollRafId = requestAnimationFrame(() => autoScroll(clientY));
            } else {
                scrollRafId = null;
            }
        }

        function getWrapperDivs() {
            return Array.from(cellList.querySelectorAll('[data-cell-index]'));
        }

        function onMouseMove(e) {
            ghost.style.left = (e.clientX - offsetX) + 'px';
            ghost.style.top = (e.clientY - offsetY) + 'px';

            autoScroll(e.clientY);

            const wrappers = getWrapperDivs();
            const listRect = cellList.getBoundingClientRect();

            // Find which slot the cursor is over
            let targetSlot = wrappers.length; // default: after last
            for (let i = 0; i < wrappers.length; i++) {
                const wr = wrappers[i].getBoundingClientRect();
                const midY = wr.top + wr.height / 2;
                if (e.clientY < midY) {
                    targetSlot = i;
                    break;
                }
            }

            // Convert slot to absolute index
            if (targetSlot >= wrappers.length) {
                const last = wrappers[wrappers.length - 1];
                currentInsertBefore = last
                    ? parseInt(last.dataset.cellIndex, 10) + 1
                    : 0;
            } else {
                currentInsertBefore = parseInt(wrappers[targetSlot].dataset.cellIndex, 10);
            }

            // Position indicator
            indicator.style.display = 'block';
            if (targetSlot === 0) {
                const firstRect = wrappers[0] && wrappers[0].getBoundingClientRect();
                indicator.style.top = firstRect
                    ? (firstRect.top - listRect.top - 1) + 'px'
                    : '0';
            } else if (targetSlot >= wrappers.length) {
                const lastRect = wrappers[wrappers.length - 1] && wrappers[wrappers.length - 1].getBoundingClientRect();
                indicator.style.top = lastRect
                    ? (lastRect.bottom - listRect.top + 1) + 'px'
                    : '0';
            } else {
                const aboveRect = wrappers[targetSlot - 1].getBoundingClientRect();
                const belowRect = wrappers[targetSlot].getBoundingClientRect();
                indicator.style.top = ((aboveRect.bottom + belowRect.top) / 2 - listRect.top) + 'px';
            }
        }

        function onMouseUp() {
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);
            if (scrollRafId) cancelAnimationFrame(scrollRafId);

            cellEl.classList.remove('verso-cell--dragging');
            ghost.remove();
            indicator.remove();
            document.body.style.cursor = prevCursor;

            // Only invoke if position actually changed
            if (currentInsertBefore !== fromIndex && currentInsertBefore !== fromIndex + 1) {
                if (_dotnetRef) {
                    _dotnetRef.invokeMethodAsync('OnCellDragDrop', fromIndex, currentInsertBefore);
                }
            }
        }

        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
    }

    return { init, dispose };
})();
