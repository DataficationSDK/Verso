/**
 * Cell interaction bridge for Verso notebooks.
 * Routes cell/interact requests to either:
 *   - VS Code extension host via vscodeBridge.sendRequest (WASM)
 *   - Blazor Server via a registered DotNetObjectReference
 */
window.versoCellInteract = (() => {
    let _dotNetRef = null;

    return {
        /**
         * Register a DotNetObjectReference for Blazor Server interop.
         * Not needed for WASM — the vscodeBridge path is used instead.
         */
        register(dotNetRef) {
            _dotNetRef = dotNetRef;
        },

        /**
         * Send a cell interaction request to the host.
         * @param {string} cellId - GUID of the cell
         * @param {string} extensionId - Extension that handles the interaction
         * @param {string} interactionType - Application-defined interaction type
         * @param {string} payloadJson - JSON payload string
         * @param {string|null} outputBlockId - Optional output block to update in place
         * @returns {Promise<string|null>} Response HTML or null
         */
        async cellInteract(cellId, extensionId, interactionType, payloadJson, outputBlockId) {
            if (window.vscodeBridge && typeof window.vscodeBridge.sendRequest === 'function') {
                // vscodeBridge.sendRequest expects a JSON string and returns a JSON string
                var paramsJson = JSON.stringify({
                    cellId,
                    extensionId,
                    interactionType,
                    payload: payloadJson,
                    outputBlockId: outputBlockId || null
                });
                var resultJson = await window.vscodeBridge.sendRequest('cell/interact', paramsJson);
                if (!resultJson) return null;
                try {
                    var result = JSON.parse(resultJson);
                    return result.response || result.Response || null;
                } catch (e) {
                    return resultJson;
                }
            }

            if (_dotNetRef) {
                return await _dotNetRef.invokeMethodAsync(
                    'OnCellInteract', cellId, extensionId, interactionType, payloadJson, outputBlockId || null);
            }

            throw new Error('No cell interaction host available.');
        }
    };
})();
