/**
 * Layout interaction bridge for Verso notebooks.
 * Routes layout/interact requests to either:
 *   - VS Code extension host via vscodeBridge.sendRequest (WASM)
 *   - Blazor Server via a registered DotNetObjectReference (placeholder for future server-side custom layouts)
 */
if (!window.versoLayoutInteract) {
    window.versoLayoutInteract = (() => {
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
             * Send a layout interaction request to the host.
             * @param {string} extensionId
             * @param {string} layoutId
             * @param {string} frameInstanceId
             * @param {string} interactionType
             * @param {string} payloadJson
             * @param {string|null} targetId
             * @returns {Promise<string|null>}
             */
            async layoutInteract(extensionId, layoutId, frameInstanceId, interactionType, payloadJson, targetId) {
                if (window.vscodeBridge && typeof window.vscodeBridge.sendRequest === 'function') {
                    const paramsJson = JSON.stringify({
                        extensionId,
                        layoutId,
                        frameInstanceId,
                        interactionType,
                        payload: payloadJson,
                        targetId: targetId || null
                    });
                    const resultJson = await window.vscodeBridge.sendRequest('layout/interact', paramsJson);
                    if (!resultJson) return null;
                    try {
                        const result = JSON.parse(resultJson);
                        return result.response || result.Response || null;
                    } catch (e) {
                        return resultJson;
                    }
                }

                if (_dotNetRef) {
                    return await _dotNetRef.invokeMethodAsync(
                        'OnLayoutInteract', extensionId, layoutId, frameInstanceId, interactionType, payloadJson, targetId || null);
                }

                throw new Error('No layout interaction host available.');
            }
        };
    })();
}
