// Panel resize interop for Blazor
window.versoPanel = {

    initResize: function (handleEl, panelEl, dotnetRef, minWidth, maxWidth) {
        if (!handleEl || !panelEl) return;

        handleEl.addEventListener('mousedown', function (e) {
            e.preventDefault();
            e.stopPropagation();

            var startX = e.clientX;
            var startWidth = panelEl.offsetWidth;

            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';

            function onMouseMove(e) {
                // Dragging left edge: moving left increases width
                var dx = startX - e.clientX;
                var newWidth = Math.max(minWidth, Math.min(maxWidth, startWidth + dx));
                panelEl.style.width = newWidth + 'px';
            }

            function onMouseUp(e) {
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);
                document.body.style.cursor = '';
                document.body.style.userSelect = '';

                var finalWidth = panelEl.offsetWidth;
                if (dotnetRef) {
                    dotnetRef.invokeMethodAsync('OnPanelResized', finalWidth);
                }
            }

            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        });
    },

    dispose: function () {
        // Cleanup handled by Blazor disposing the DotNetObjectReference
    }
};
