/**
 * Event delegation for interactive parameters cell forms.
 * Captures user interactions on rendered parameter forms and routes them
 * through versoCellInteract.cellInteract() to the engine-side handler.
 */
window.versoParameters = (() => {
    const EXTENSION_ID = 'verso.renderer.parameters';

    function getCellId(el) {
        const container = el.closest('[data-cell-id]');
        return container ? container.getAttribute('data-cell-id') : null;
    }

    function replaceFormOutput(cellId, html) {
        const container = document.querySelector(`[data-cell-id="${cellId}"]`);
        if (container) {
            const outputDiv = container.querySelector('.verso-output--html') || container;
            outputDiv.innerHTML = html;
        }
    }

    async function sendInteraction(cellId, interactionType, payload) {
        try {
            const response = await versoCellInteract.cellInteract(
                cellId, EXTENSION_ID, interactionType, JSON.stringify(payload), null);
            if (response) {
                replaceFormOutput(cellId, response);
            }
        } catch (e) {
            console.error('Parameters interaction failed:', e);
        }
    }

    function getAddRow(cellEl) {
        return cellEl ? cellEl.querySelector('.verso-parameter-add-row') : null;
    }

    // --- Event delegation ---

    document.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;
        const action = btn.getAttribute('data-action');
        const cellId = getCellId(btn);
        if (!cellId) return;

        if (action === 'parameter-remove') {
            const paramName = btn.getAttribute('data-param');
            if (paramName) {
                sendInteraction(cellId, 'parameter-remove', { name: paramName });
            }
        } else if (action === 'parameter-add') {
            // Show the inline add row (and its parent table if hidden, e.g. in empty state)
            const cell = btn.closest('[data-cell-id]');
            const addRow = getAddRow(cell);
            if (addRow) {
                const table = addRow.closest('table');
                if (table) table.style.display = '';
                addRow.style.display = '';
                btn.style.display = 'none';
                const nameInput = addRow.querySelector('.verso-add-name');
                if (nameInput) nameInput.focus();
            }
        } else if (action === 'parameter-confirm-add') {
            // Collect values from inline row and send interaction
            const cell = btn.closest('[data-cell-id]');
            const addRow = getAddRow(cell);
            if (!addRow) return;
            const name = (addRow.querySelector('.verso-add-name')?.value || '').trim();
            if (!name) {
                addRow.querySelector('.verso-add-name')?.focus();
                return;
            }
            const type = addRow.querySelector('.verso-add-type')?.value || 'string';
            const description = addRow.querySelector('.verso-add-description')?.value || '';
            const defaultValue = addRow.querySelector('.verso-add-default')?.value || '';
            const required = addRow.querySelector('.verso-add-required')?.checked || false;
            sendInteraction(cellId, 'parameter-add', { name, type, description, defaultValue, required });
        } else if (action === 'parameter-cancel-add') {
            // Hide the inline add row and show the button again
            const cell = btn.closest('[data-cell-id]');
            const addRow = getAddRow(cell);
            if (addRow) {
                addRow.style.display = 'none';
                // Clear inputs
                const nameInput = addRow.querySelector('.verso-add-name');
                if (nameInput) nameInput.value = '';
                const descInput = addRow.querySelector('.verso-add-description');
                if (descInput) descInput.value = '';
                const defaultInput = addRow.querySelector('.verso-add-default');
                if (defaultInput) defaultInput.value = '';
                const reqInput = addRow.querySelector('.verso-add-required');
                if (reqInput) reqInput.checked = false;
                const typeSelect = addRow.querySelector('.verso-add-type');
                if (typeSelect) typeSelect.value = 'string';
            }
            const addBtn = cell?.querySelector('[data-action="parameter-add"]');
            if (addBtn) addBtn.style.display = '';
        }
    });

    // Allow Enter key in the name field to confirm the add, and in
    // parameter value fields to commit the update immediately.
    document.addEventListener('keydown', (e) => {
        if (e.key !== 'Enter') return;

        // Add-row name field: confirm the add
        const addInput = e.target.closest('.verso-add-name');
        if (addInput) {
            const addRow = addInput.closest('.verso-parameter-add-row');
            const confirmBtn = addRow?.querySelector('[data-action="parameter-confirm-add"]');
            if (confirmBtn) confirmBtn.click();
            return;
        }

        // Parameter value fields: commit the update on Enter so the
        // server-side model is updated even if the browser does not
        // fire a change event for inputs outside a <form>.
        const paramInput = e.target.closest('[data-action="parameter-update"]');
        if (paramInput && paramInput.type !== 'checkbox') {
            e.preventDefault();
            const cellId = getCellId(paramInput);
            const paramName = paramInput.getAttribute('data-param');
            if (!cellId || !paramName) return;
            sendInteraction(cellId, 'parameter-update', { name: paramName, value: paramInput.value });
        }
    });

    document.addEventListener('change', (e) => {
        // Handle default value updates
        const input = e.target.closest('[data-action="parameter-update"]');
        if (input) {
            const cellId = getCellId(input);
            const paramName = input.getAttribute('data-param');
            if (!cellId || !paramName) return;

            let value;
            if (input.type === 'checkbox') {
                value = input.checked ? 'true' : 'false';
            } else {
                value = input.value;
            }

            sendInteraction(cellId, 'parameter-update', { name: paramName, value });
            return;
        }

        // Handle required checkbox toggle
        const reqInput = e.target.closest('[data-action="parameter-toggle-required"]');
        if (reqInput) {
            const cellId = getCellId(reqInput);
            const paramName = reqInput.getAttribute('data-param');
            if (!cellId || !paramName) return;
            sendInteraction(cellId, 'parameter-toggle-required', {
                name: paramName,
                value: reqInput.checked ? 'true' : 'false'
            });
        }
    });

    return {};
})();

/**
 * Generic [data-action] event delegation for extension-rendered custom layouts.
 * Walks DOM ancestry to route clicks/changes/keydowns to either cell/interact
 * or layout/interact based on the nearest [data-cell-id] or [data-layout-id] ancestor.
 * Coexists with the parameter-specific handlers above; elements without a
 * [data-extension-id] ancestor are left alone so the legacy parameter path
 * continues to operate unaffected.
 */
(() => {
    if (window.__versoActionRouterAttached) return;
    window.__versoActionRouterAttached = true;

    function getPayload(actionable) {
        const explicit = actionable.getAttribute('data-payload');
        if (explicit !== null) return explicit;
        const tag = actionable.tagName;
        if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') {
            if (actionable.type === 'checkbox' || actionable.type === 'radio') {
                return actionable.checked ? 'true' : 'false';
            }
            return actionable.value != null ? String(actionable.value) : '';
        }
        return '';
    }

    function routeAction(target) {
        const actionable = target && target.closest ? target.closest('[data-action]') : null;
        if (!actionable) return;
        const interactionType = actionable.getAttribute('data-action');
        if (!interactionType) return;

        const cellAncestor = actionable.closest('[data-cell-id]');
        const layoutAncestor = actionable.closest('[data-layout-id]');

        let cellWins = false;
        if (cellAncestor) {
            if (!layoutAncestor || layoutAncestor.contains(cellAncestor)) {
                cellWins = true;
            }
        }

        if (cellWins) {
            const extEl = actionable.closest('[data-extension-id]');
            if (!extEl) return; // legacy parameter handlers cover this case
            const cellId = cellAncestor.getAttribute('data-cell-id');
            const extensionId = extEl.getAttribute('data-extension-id');
            const payload = getPayload(actionable);
            // An extension marks the output it wants replaced with data-output-id, which is what
            // the host searches its rendered content for. Without a match the whole cell's output
            // is replaced instead, so the name has to be the one the host looks for.
            const outputEl = actionable.closest('[data-output-id]');
            const outputBlockId = outputEl ? outputEl.getAttribute('data-output-id') : null;
            if (window.versoCellInteract && typeof window.versoCellInteract.cellInteract === 'function') {
                window.versoCellInteract
                    .cellInteract(cellId, extensionId, interactionType, payload, outputBlockId)
                    .catch(err => console.error('cell/interact failed:', err));
            }
            return;
        }

        if (layoutAncestor) {
            if (!layoutAncestor.hasAttribute('data-extension-id')) {
                console.warn(
                    'Verso layout action ignored: data-extension-id and data-layout-id must be on the same element',
                    actionable);
                return;
            }
            const extensionId = layoutAncestor.getAttribute('data-extension-id');
            const layoutId = layoutAncestor.getAttribute('data-layout-id');
            const frameInstanceId = layoutAncestor.getAttribute('data-frame-instance-id') || '';
            const payload = getPayload(actionable);
            const targetId = actionable.getAttribute('data-target-id');
            if (window.versoLayoutInteract && typeof window.versoLayoutInteract.layoutInteract === 'function') {
                window.versoLayoutInteract
                    .layoutInteract(extensionId, layoutId, frameInstanceId, interactionType, payload, targetId)
                    .catch(err => console.error('layout/interact failed:', err));
            }
            return;
        }

        // Neither a cell nor a layout ancestor: nothing to route to.
        // Stay silent so unrelated [data-action] usages elsewhere on the page don't spam the console.
    }

    document.body.addEventListener('click', (e) => routeAction(e.target));
    document.body.addEventListener('change', (e) => routeAction(e.target));
    document.body.addEventListener('keydown', (e) => routeAction(e.target));
})();
