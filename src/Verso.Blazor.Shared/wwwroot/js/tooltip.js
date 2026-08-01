/**
 * Tooltips for controls that show an icon instead of a label.
 *
 * The browser's own tooltip is not good enough for an icon-only control: it waits
 * about a second, never appears on keyboard focus, cannot be themed, and has room
 * for a name but not for an explanation. This replaces it.
 *
 * A control opts in with data-verso-tip, and may add data-verso-tip-desc for a
 * second line. The description is optional by design: most controls have nothing
 * to say beyond their name, and a tooltip that pads them out with restated text
 * is worse than a short one.
 *
 *     <button data-verso-tip="Compare"
 *             data-verso-tip-desc="Measure this notebook against a saved baseline.">
 *
 * The delay is not what makes this feel fast. A group of controls warms up: the
 * first hover waits, and every sibling after it answers immediately until the
 * pointer has been away from the group for a moment. Sweeping across a toolbar
 * therefore costs one wait rather than one per button. Groups are marked with
 * data-verso-tip-group; without one, an element's parent is its group.
 *
 * There is no .NET interop here. Everything is delegated from the document, so
 * markup rendered by Blazor, by a custom layout, or by an extension all behave
 * the same, and re-rendering a control does not need to re-register anything.
 */
window.versoTooltip = (() => {
    const SHOW_DELAY_MS = 220;
    // How long a group stays warm after the pointer leaves it. Long enough to
    // cover a glance away and back, short enough that returning much later still
    // reads as a fresh, deliberate hover.
    const COOL_DOWN_MS = 500;
    const VIEWPORT_MARGIN = 8;
    const GAP = 8;
    const TIP_ID = 'verso-tooltip';

    let tip = null;
    let anchor = null;
    let warmGroup = null;
    let showTimer = 0;
    let coolTimer = 0;
    let aliveTimer = 0;
    // Set on click, cleared on the next real pointer movement.
    let suppressed = false;

    function element() {
        if (tip && tip.isConnected) return tip;
        tip = document.createElement('div');
        tip.id = TIP_ID;
        tip.className = 'verso-tooltip';
        tip.setAttribute('role', 'tooltip');
        tip.dataset.shown = 'false';
        document.body.appendChild(tip);
        return tip;
    }

    function groupOf(el) {
        return el.closest('[data-verso-tip-group]') || el.parentElement;
    }

    /**
     * Whether a tooltip would only repeat what the control already says.
     *
     * A label that is on screen does not need a tooltip restating it: the kernel
     * pill reading "Idle" has nothing to add by saying "Idle" again. Suppressing
     * these here rather than at each call site keeps the rule in one place, and
     * keeps it correct as the control changes: the same pill hides its text in a
     * narrow pane, and at that width the tooltip is the only way to read it.
     *
     * Three things keep this from suppressing something useful. A description is
     * never redundant, since it is by definition not on the button. Text clipped
     * to an ellipsis is not readable even though it is present, which is the usual
     * reason a control repeats its own label. And an element whose width cannot be
     * measured is left alone rather than guessed at.
     */
    function repeatsVisibleText(el, name, desc) {
        if (desc) return false;

        const text = (el.innerText || '').trim().replace(/\s+/g, ' ');
        if (!text) return false;
        if (text.toLowerCase() !== name.trim().toLowerCase()) return false;

        if (!el.clientWidth) return false;
        return el.scrollWidth <= el.clientWidth;
    }

    function targetFrom(node) {
        if (!node || typeof node.closest !== 'function') return null;
        const el = node.closest('[data-verso-tip]');
        if (!el) return null;

        // A control whose menu is open has already been understood and acted on, and
        // its menu is drawn in the top layer where no tooltip can sit in front of it.
        if (el.getAttribute('aria-expanded') === 'true') return null;

        // An empty name is how a control says "nothing to show right now" without
        // the caller having to conditionally emit the attribute.
        const name = el.getAttribute('data-verso-tip');
        if (!name) return null;

        return repeatsVisibleText(el, name, el.getAttribute('data-verso-tip-desc'))
            ? null
            : el;
    }

    function render(el) {
        const t = element();
        const name = el.getAttribute('data-verso-tip') || '';
        const desc = el.getAttribute('data-verso-tip-desc') || '';
        t.textContent = '';

        const nameEl = document.createElement('div');
        nameEl.className = 'verso-tooltip-name';
        nameEl.textContent = name;
        t.appendChild(nameEl);

        if (desc) {
            const descEl = document.createElement('div');
            descEl.className = 'verso-tooltip-desc';
            descEl.textContent = desc;
            t.appendChild(descEl);
        }
    }

    function place(el) {
        const t = element();
        // Measured at the origin so the size is read without the previous
        // position constraining it.
        t.style.left = '0px';
        t.style.top = '0px';

        const a = el.getBoundingClientRect();
        const r = t.getBoundingClientRect();

        let left = a.left + a.width / 2 - r.width / 2;
        left = Math.max(VIEWPORT_MARGIN,
            Math.min(left, window.innerWidth - r.width - VIEWPORT_MARGIN));

        // Below by default, above when there is not room. Toolbar controls sit at
        // the top of the window, so below is almost always the answer; cell and
        // panel controls near the bottom are why the flip exists.
        let top = a.bottom + GAP;
        if (top + r.height > window.innerHeight - VIEWPORT_MARGIN && a.top - GAP - r.height >= VIEWPORT_MARGIN) {
            top = a.top - GAP - r.height;
        }

        t.style.left = `${Math.round(left)}px`;
        t.style.top = `${Math.round(top)}px`;
    }

    function show(el, instant) {
        const t = element();
        anchor = el;
        render(el);
        // A warm tooltip skips the fade as well as the delay. Once someone is
        // reading labels, the fade is the only thing left that feels slow.
        t.dataset.warm = instant ? 'true' : 'false';
        place(el);
        t.dataset.shown = 'true';
        warmGroup = groupOf(el);

        // The tooltip is decoration over a control that already carries its own
        // accessible name, so it describes rather than labels.
        el.setAttribute('aria-describedby', TIP_ID);

        // A control can be re-rendered or removed out from under a shown tooltip,
        // in which case no pointer event ever arrives to dismiss it.
        clearInterval(aliveTimer);
        aliveTimer = setInterval(() => {
            // Gone, or it just opened a menu that is drawn in front of this tooltip.
            if (!anchor || !anchor.isConnected) hide();
            else if (anchor.getAttribute('aria-expanded') === 'true') hide();
        }, 250);
    }

    function hide() {
        clearTimeout(showTimer);
        showTimer = 0;
        clearInterval(aliveTimer);
        aliveTimer = 0;
        if (anchor) {
            anchor.removeAttribute('aria-describedby');
            anchor = null;
        }
        if (tip) tip.dataset.shown = 'false';
    }

    function enter(el, viaFocus) {
        if (el === anchor) return;
        if (suppressed && !viaFocus) return;
        clearTimeout(coolTimer);
        clearTimeout(showTimer);

        // A focus is never accidental, and neither is a hover inside a group the
        // user is already reading. Both answer at once.
        if (viaFocus || warmGroup === groupOf(el)) {
            show(el, true);
            return;
        }
        showTimer = setTimeout(() => show(el, false), SHOW_DELAY_MS);
    }

    function leave(el) {
        if (anchor && anchor !== el) return;
        hide();
        clearTimeout(coolTimer);
        const group = groupOf(el);
        coolTimer = setTimeout(() => {
            if (warmGroup === group) warmGroup = null;
        }, COOL_DOWN_MS);
    }

    document.addEventListener('pointerover', (ev) => {
        // Touch has no hover, and a long press producing a tooltip over the thing
        // just pressed is not useful.
        if (ev.pointerType === 'touch') return;

        const el = targetFrom(ev.target);
        if (el) {
            enter(el, false);
        } else if (anchor && !anchor.contains(ev.target)) {
            leave(anchor);
        }
    }, true);

    document.addEventListener('pointerout', (ev) => {
        if (ev.pointerType === 'touch') return;
        const el = targetFrom(ev.target);
        if (el && !el.contains(ev.relatedTarget)) leave(el);
    }, true);

    // Only keyboard focus speaks. Clicking a control focuses it too, and a focus that
    // arrives as a side effect of a click must not undo the click's suppression: it
    // would re-show the tooltip the click just dismissed, in front of whatever the
    // click opened. ':focus-visible' is exactly that distinction, and engines without
    // it throw on matches() rather than returning false.
    function isKeyboardFocus(el) {
        try { return el.matches(':focus-visible'); }
        catch (e) { return true; }
    }

    document.addEventListener('focusin', (ev) => {
        const el = targetFrom(ev.target);
        if (!el) {
            if (anchor) hide();
            return;
        }
        if (isKeyboardFocus(el)) enter(el, true);
    }, true);

    document.addEventListener('focusout', () => hide(), true);

    // Whatever the control does, it is more interesting than its own name. Staying
    // hidden until the pointer moves again matters as much as hiding: clicking a
    // control re-renders it under a stationary cursor, and the browser answers that
    // with a fresh pointerover that would otherwise re-show the tooltip at once.
    document.addEventListener('pointerdown', () => {
        suppressed = true;
        hide();
    }, true);

    document.addEventListener('pointermove', () => {
        if (suppressed) suppressed = false;
    }, true);

    // Escape dismisses; Enter and Space activate, and activation is as good a reason
    // to stop explaining the control as a click is.
    document.addEventListener('keydown', (ev) => {
        if (ev.key === 'Escape' || ev.key === 'Enter' || ev.key === ' ') hide();
    }, true);

    // Capture, so this catches any scrolling ancestor and not just the window.
    // The toolbar itself scrolls horizontally in a narrow pane.
    document.addEventListener('scroll', () => {
        if (anchor) place(anchor);
    }, true);

    window.addEventListener('blur', () => hide());
    window.addEventListener('resize', () => hide());

    return {
        /** Dismisses any visible tooltip. For hosts that take over the pointer. */
        hide,

        /** Current state, for tests and diagnostics. */
        state() {
            return {
                shown: !!tip && tip.dataset.shown === 'true',
                name: anchor ? anchor.getAttribute('data-verso-tip') : null,
                warm: !!tip && tip.dataset.warm === 'true',
            };
        },
    };
})();
