/**
 * Tag input interop for cell properties panel.
 * Intercepts comma and Enter keys synchronously in the browser to
 * call preventDefault() before the character is inserted into the input.
 * This eliminates the race condition between keydown, oninput, and @bind
 * that exists in both Blazor Server (SignalR latency) and WASM (async yield).
 */
window.versoTagInput = {
    /**
     * Attach a keydown listener to a tag input element.
     * @param {HTMLInputElement} element - The input element ref
     * @param {DotNetObjectReference} dotNetRef - C# object with [JSInvokable] methods
     */
    attach(element, dotNetRef) {
        if (!element || element.__versoTagHandler) return;

        function handler(e) {
            if (e.key === ',' || e.key === 'Enter') {
                e.preventDefault();
                var text = element.value.trim();
                if (text) {
                    element.value = '';
                    dotNetRef.invokeMethodAsync('JsAddTag', text);
                }
            }
        }

        element.__versoTagHandler = handler;
        element.__versoTagDotNetRef = dotNetRef;
        element.addEventListener('keydown', handler);
    },

    /**
     * Detach the keydown listener and release the DotNetObjectReference.
     * @param {HTMLInputElement} element - The input element ref
     */
    detach(element) {
        if (!element || !element.__versoTagHandler) return;
        element.removeEventListener('keydown', element.__versoTagHandler);
        element.__versoTagHandler = null;
        element.__versoTagDotNetRef = null;
    }
};
