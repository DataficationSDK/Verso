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

    // Allow Enter key in the name field to confirm the add
    document.addEventListener('keydown', (e) => {
        if (e.key !== 'Enter') return;
        const input = e.target.closest('.verso-add-name');
        if (!input) return;
        const addRow = input.closest('.verso-parameter-add-row');
        const confirmBtn = addRow?.querySelector('[data-action="parameter-confirm-add"]');
        if (confirmBtn) confirmBtn.click();
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
