/**
 * Panel interaction bridge.
 *
 * An extension panel's content is markup the extension produced, so its buttons are
 * not Blazor components and cannot carry @onclick. This delegates from the panel
 * root instead: any element carrying data-panel-action, or a descendant of one,
 * reports the action back to the owning extension.
 *
 * Elements marked data-panel-passive are ignored, which is how a panel keeps a link
 * or a text selection inside an actionable row from firing the row's action.
 */
window.versoPanelInteract = (() => {
    const roots = new WeakMap();

    function findAction(target, root) {
        let node = target;
        while (node && node !== root) {
            if (node.nodeType === 1) {
                if (node.hasAttribute('data-panel-passive')) return null;
                if (node.hasAttribute('data-panel-action')) return node;
            }
            node = node.parentNode;
        }
        return null;
    }

    function payloadFor(el, ev) {
        // A value-bearing control reports its value; everything else reports the
        // panel's own data-panel-payload, which is whatever the extension put there.
        if (ev.type === 'change' && 'value' in el) {
            if (el.type === 'checkbox') return JSON.stringify({ value: el.checked });
            return JSON.stringify({ value: el.value });
        }
        return el.getAttribute('data-panel-payload') || '';
    }

    function handler(root, dotNetRef) {
        return (ev) => {
            const el = findAction(ev.target, root);
            if (!el) return;

            // Only intercept changes on the control that carries the action, so a
            // panel can host a form without every keystroke inside it firing.
            if (ev.type === 'change' && el !== ev.target) return;

            ev.preventDefault();
            dotNetRef.invokeMethodAsync(
                'OnPanelAction',
                el.getAttribute('data-panel-action') || '',
                el.getAttribute('data-target-id'),
                payloadFor(el, ev));
        };
    }

    return {
        /**
         * Begin delegating clicks and changes inside a panel root.
         * Re-registering on the same element replaces the previous listeners, so
         * this is safe to call after every re-render.
         */
        attach(root, dotNetRef) {
            if (!root) return;
            this.detach(root);
            const fn = handler(root, dotNetRef);
            root.addEventListener('click', fn);
            root.addEventListener('change', fn);
            roots.set(root, fn);
        },

        detach(root) {
            if (!root) return;
            const fn = roots.get(root);
            if (!fn) return;
            root.removeEventListener('click', fn);
            root.removeEventListener('change', fn);
            roots.delete(root);
        }
    };
})();
