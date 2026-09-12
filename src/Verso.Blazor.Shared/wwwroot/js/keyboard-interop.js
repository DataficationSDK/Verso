/**
 * Tells the notebook page whether the keyboard currently belongs to a text field.
 *
 * The notebook container handles Jupyter-style command-mode keys ("a" inserts a cell
 * above, "b" below, "x" and "dd" delete, the arrows move the selection) for any keydown
 * that bubbles up to it. A code editor reports its own focus to the page, but a plain
 * HTML field does not: a parameters cell's name and value inputs, a panel's property
 * fields, the toolbar's path box, or a form inside an HTML output. The page asks here
 * before it reads a key as a command, so typing into any of those never mutates the
 * notebook.
 */
window.versoKeyboard = {
    isEditableElementFocused: function () {
        var el = document.activeElement;
        // Focus inside a shadow tree shows up as the host element from the document.
        while (el && el.shadowRoot && el.shadowRoot.activeElement) {
            el = el.shadowRoot.activeElement;
        }
        if (!el || el === document.body) return false;
        var tag = el.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
        return !!el.isContentEditable;
    }
};
