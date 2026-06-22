using System.Text;

namespace Verso.Extensions.Layouts;

/// <summary>
/// The CSS and JavaScript the built-in notebook layout ships to the host via
/// <c>ILayoutEngine.GetStaticAssetsAsync</c>. The CSS is fully self-scoped: every selector is
/// prefixed with the layout-root attributes so the styling cannot leak past this layout into the
/// host page or a sibling layout.
///
/// Two things give the layout its look without touching the host:
/// 1. A local set of <c>--md-*</c> design tokens (color roles, elevation, shape) all derived from
///    the host theme tokens (<c>--verso-*</c>) with sensible fallbacks, so the layout follows the
///    active theme and re-paints on its own when the host switches between light, dark, and
///    high-contrast.
/// 2. A small set of <c>--verso-*</c> token overrides set on the layout root. Because the live cell
///    component is physically moved (portaled) inside the layout root, those custom properties
///    cascade into it, so the editor chrome, buttons, and outputs pick up the same shape and
///    surfaces too — the cell stays fully live and editable, it just adopts the layout's styling.
/// </summary>
internal static class NotebookLayoutStyles
{
    /// <summary>
    /// Builds the layout-scoped stylesheet. <paramref name="root"/> is the layout-root selector
    /// prefix (ending with a space).
    /// </summary>
    public static string BuildCss(string root)
    {
        var sb = new StringBuilder(12 * 1024);

        void R(string selector, string body)
        {
            var parts = selector.Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(root).Append(parts[i].Trim());
            }
            sb.Append(" { ").Append(body).Append(" }\n");
        }

        // Raw rules (keyframes, media queries) are emitted without the descendant prefix since a
        // descendant-scoped @-rule would be dropped by the CSS parser. Names are namespaced with
        // "vmd-" to avoid colliding with the host or another layout.
        void Raw(string css) => sb.Append(css).Append('\n');

        // --- Root surface + design tokens ---------------------------------------------------
        // Color roles resolve from the host palette. Each role prefers the coarse host
        // token and falls back to a finer one that is always present, then a literal, so the
        // layout stays blended even on a host whose theme bridge hasn't mapped the coarse palette.
        R(".vmd-root",
            "position:relative; box-sizing:border-box; min-height:100%;" +
            "display:flex; flex-direction:column; gap:0; padding:16px 0 64px;" +
            "font-family:var(--verso-ui-font-family, Roboto, system-ui, -apple-system, 'Segoe UI', sans-serif);" +
            "color:var(--md-on-surface);" +
            // Color roles.
            "--md-surface:var(--verso-bg-default, var(--verso-editor-background, #fff));" +
            "--md-surface-container:var(--verso-bg-elevated, var(--verso-toolbar-background, #f3f3f3));" +
            "--md-on-surface:var(--verso-fg-default, var(--verso-editor-foreground, #1c1b1f));" +
            "--md-on-surface-variant:var(--verso-fg-muted, var(--verso-editor-line-number, #49454f));" +
            "--md-primary:var(--verso-accent, var(--verso-accent-primary, #6750a4));" +
            "--md-on-primary:var(--md-surface);" +
            "--md-outline:var(--verso-border-default, var(--verso-cell-border, rgba(0,0,0,0.16)));" +
            "--md-outline-variant:color-mix(in srgb, var(--md-outline) 55%, transparent);" +
            "--md-error:var(--verso-status-error, #b3261e);" +
            // Elevation (shadow). Two levels: resting card and raised card.
            "--md-elev-1:0 1px 2px rgba(0,0,0,0.18), 0 1px 3px 1px rgba(0,0,0,0.10);" +
            "--md-elev-2:0 2px 4px rgba(0,0,0,0.18), 0 4px 8px 3px rgba(0,0,0,0.12);" +
            // Shape scale.
            "--md-shape-sm:8px; --md-shape-md:12px; --md-shape-lg:16px; --md-shape-full:999px;" +
            // Background tracks the host canvas.
            "background:var(--md-surface);" +
            // --- Token overrides that cascade into the portaled live cell ---
            // The card is the visual surface for each cell, so the cell's own background
            // and border are removed and its shape rounded.
            // NOTE: do NOT override --verso-button-border-radius here. The cell's language and
            // cell-type popup menus take their border-radius from that token, so a large (pill)
            // value distorts the menus into clipped ovals. The trigger badges use a hardcoded
            // radius, so leaving the token at the host default keeps the menus looking right.
            "--verso-cell-background:transparent;" +
            "--verso-cell-border:transparent;" +
            "--verso-cell-hover-background:transparent;" +
            "--verso-cell-border-radius:var(--md-shape-md);" +
            // The card itself carries the running signal (accent border + stripe), so the cell's
            // own running-indicator border is suppressed to avoid a second, inner border while a
            // cell executes.
            "--verso-cell-running-indicator:transparent;" +
            "--verso-cell-output-background:color-mix(in srgb, var(--md-on-surface) 4%, var(--md-surface));");

        // --- Cell list ----------------------------------------------------------------------
        // Full available width with the host's content margin, not a narrow centered column.
        R(".vmd-cells",
            "display:flex; flex-direction:column; gap:0;" +
            "width:100%; padding:0 var(--verso-content-margin-horizontal, 24px);");

        // Each cell is an elevated surface card. The live cell component is portaled in.
        // No overflow:hidden here: the portaled cell's own popups (the language and cell-type
        // pickers) extend past the card edge, and clipping them to the rounded card crops the
        // menu. The card stays visually rounded via its border, background, and the inset stripe.
        R(".vmd-cell",
            "position:relative; border-radius:var(--md-shape-lg); background:var(--md-surface);" +
            "border:1px solid var(--md-outline-variant); box-shadow:var(--md-elev-1);" +
            "transition:box-shadow 160ms ease, border-color 160ms ease;");
        R(".vmd-cell:hover", "box-shadow:var(--md-elev-2);");
        // A leading accent stripe distinguishes hover/running state. It is inset
        // from the rounded corners and rounded itself so it reads cleanly without overflow:hidden.
        R(".vmd-cell::before",
            "content:''; position:absolute; left:0; top:10px; bottom:10px; width:3px; opacity:0;" +
            "border-radius:0 3px 3px 0; background:var(--md-primary); transition:opacity 160ms ease;");
        R(".vmd-cell:hover::before, .vmd-cell.is-running::before, .vmd-cell:has(.verso-cell--executing)::before", "opacity:1;");
        // Running cell: tonal accent border + animated stripe so progress reads at a glance. The
        // card responds both to the script's is-running marker and, as a robust fallback driven by
        // the cell's real execution state, to a descendant cell carrying verso-cell--executing.
        R(".vmd-cell.is-running, .vmd-cell:has(.verso-cell--executing)", "border-color:var(--md-primary); box-shadow:var(--md-elev-2);");
        R(".vmd-cell.is-running::before, .vmd-cell:has(.verso-cell--executing)::before", "animation:vmd-run-pulse 1200ms ease-in-out infinite;");
        // Give the portaled cell room to breathe inside the card.
        R(".vmd-cell > *", "padding:4px 6px;");
        // An unfilled slot (one the portal has not mounted its live cell into yet) still carries the
        // card's border and shadow but has no content, so it collapses to a thin horizontal bar that
        // reads as a stray line. That happens for the moment a slot exists without its cell: just
        // after an insert (the new slot renders a beat before the cell is portaled in) and just after
        // a delete (the cell node is pulled immediately, but the slot lingers until the next layout
        // re-render). Hide the empty slot so neither transition flashes a line.
        R(".vmd-cell[data-cell-slot]:not(:has(> [data-cell-id]))", "display:none;");

        // --- Editor: flatten into the card -------------------------------------------------
        // The live cell's Monaco editor normally draws its own 1px border, which reads as a
        // box-inside-the-card. Inside this layout the card already IS the surface, so the editor
        // sits flush (border kept but made transparent, so there is no layout shift). To keep the
        // click target discoverable without that constant outline, a faint border fades in on
        // hover (right as the pointer arrives) and a stronger one marks the selected cell, so you
        // can always tell where the editor is and which one has focus.
        R(".verso-monaco-editor",
            "border-color:transparent; border-radius:var(--md-shape-sm);" +
            "transition:border-color 140ms ease;");
        R(".verso-monaco-editor:hover", "border-color:var(--md-outline-variant);");
        R(".verso-cell--selected .verso-monaco-editor", "border-color:var(--md-outline);");
        // Monaco draws a 1px outline around the active line (the built-in theme's
        // lineHighlightBorder). On the top line its edge pokes into the scrollbar lane and past
        // the editor's rounded corner, which reads as the selected line overlapping the scroll
        // area. Drop that outline here; the active line stays marked by its highlighted line
        // number in the gutter. Scoped to this layout so other layouts keep Monaco's default.
        R(".monaco-editor .view-overlays .current-line", "border-color:transparent;");

        // --- Always-on type / language badges ----------------------------------------------
        // The cell's type and language badges normally live in a toolbar that only appears on
        // hover or selection. Keep them visible at all times so the cell's kind and language are
        // always legible, but keep the run/move/delete action buttons hover-only so the resting
        // chrome stays quiet.
        R(".verso-cell-toolbar", "opacity:1; pointer-events:auto;");
        R(".verso-cell-toolbar-actions",
            "opacity:0; pointer-events:none; transition:opacity 140ms ease;");
        R(".verso-cell:hover .verso-cell-toolbar-actions," +
          ".verso-cell--selected .verso-cell-toolbar-actions," +
          ".verso-cell--executing .verso-cell-toolbar-actions",
            "opacity:1; pointer-events:auto;");

        // --- Rendered markdown: chromeless, reads as document prose -------------------------
        // A markdown (or any preview) cell that has been run shows its rendered content. The cell
        // component already strips its own border/background in that state, but the layout card
        // would still box it. Drop the card chrome so the rendered content flows like prose, and
        // keep the badges quiet there to avoid floating chrome over the text. Hovering restores
        // the card surface, badges, and actions so it's clearly an editable cell.
        R(".vmd-cell:has(.verso-cell--preview)",
            "background:transparent; border-color:transparent; box-shadow:none;");
        R(".vmd-cell:has(.verso-cell--preview):hover",
            "background:var(--md-surface); border-color:var(--md-outline-variant); box-shadow:var(--md-elev-1);");
        R(".verso-cell--preview .verso-cell-toolbar", "opacity:0; pointer-events:none;");
        R(".verso-cell--preview:hover .verso-cell-toolbar," +
          ".verso-cell--preview:hover .verso-cell-toolbar-actions",
            "opacity:1; pointer-events:auto;");

        // --- Drag to reorder ----------------------------------------------------------------
        // Dragging starts from the cell gutter (the run/index rail).
        // The script drives the reorder via a move-cell interaction; these rules are the cursor
        // hint, the dragged-cell treatment, and the drop-position indicator line.
        R(".vmd-cell .verso-cell-gutter", "cursor:grab;");
        R(".vmd-root.vmd-is-dragging", "cursor:grabbing; user-select:none;");
        R(".vmd-cell.vmd-dragging", "opacity:0.5;");
        R(".vmd-cell.vmd-drop-before", "box-shadow:0 -3px 0 0 var(--md-primary), var(--md-elev-1);");
        R(".vmd-cell.vmd-drop-after", "box-shadow:0 3px 0 0 var(--md-primary), var(--md-elev-1);");

        // --- Insert rail (between cells) ----------------------------------------------------
        // Compact at rest so cells sit close together; a brief hover over the gap expands the
        // rail and fades in the per-type insert buttons. The reveal is delayed so a quick mouse
        // pass across the gap doesn't pop the affordance, while leaving collapses it immediately
        // (the delay lives only on the :hover rules).
        R(".vmd-insert",
            "display:flex; align-items:center; justify-content:center; height:8px;" +
            "position:relative; transition:height 140ms ease;");
        // A transparent band slightly taller than the rail makes the thin resting gap easy to
        // hover without enlarging the visible spacing between cells.
        R(".vmd-insert::after",
            "content:''; position:absolute; left:0; right:0; top:-4px; bottom:-4px;");
        R(".vmd-insert-buttons",
            "position:relative; z-index:1; display:inline-flex; gap:6px; opacity:0;" +
            "pointer-events:none; transition:opacity 140ms ease;");
        // Reveal on a brief hover. The transition-delay sits only on the hover state, so the
        // affordance appears after a short pause but collapses instantly when the pointer leaves.
        R(".vmd-insert:hover", "height:32px; transition-delay:400ms;");
        R(".vmd-insert:hover .vmd-insert-buttons",
            "opacity:1; pointer-events:auto; transition-delay:400ms;");
        R(".vmd-insert-btn",
            "cursor:pointer; display:inline-flex; align-items:center; gap:4px;" +
            "padding:3px 12px; border-radius:var(--md-shape-full); font-family:inherit; font-size:12px;" +
            "font-weight:500; border:1px solid var(--md-outline); color:var(--md-primary);" +
            "background:var(--md-surface); box-shadow:var(--md-elev-1);" +
            "transition:background 140ms ease, border-color 140ms ease;");
        R(".vmd-insert-btn:hover", "background:color-mix(in srgb, var(--md-primary) 14%, var(--md-surface)); border-color:var(--md-primary);");
        R(".vmd-insert-glyph", "font-size:13px; line-height:1;");

        // --- Bottom add row -----------------------------------------------------------------
        R(".vmd-add-row",
            "display:flex; flex-wrap:wrap; gap:10px; justify-content:center;" +
            "width:100%; margin:20px 0 0; padding:0 var(--verso-content-margin-horizontal, 24px);");
        R(".vmd-add-btn",
            "cursor:pointer; display:inline-flex; align-items:center; gap:6px;" +
            "padding:8px 18px; border-radius:var(--md-shape-full); font-family:inherit; font-size:13px;" +
            "font-weight:500; border:1px solid var(--md-outline); color:var(--md-on-surface);" +
            "background:var(--md-surface-container);" +
            "transition:background 140ms ease, border-color 140ms ease, box-shadow 140ms ease;");
        R(".vmd-add-btn:hover",
            "background:color-mix(in srgb, var(--md-primary) 12%, var(--md-surface-container));" +
            "border-color:var(--md-primary); box-shadow:var(--md-elev-1);");
        R(".vmd-add-glyph", "font-size:15px; line-height:1; color:var(--md-primary);");

        // --- Empty state --------------------------------------------------------------------
        R(".vmd-empty",
            "text-align:center; padding:48px 24px; color:var(--md-on-surface-variant);" +
            "border:1px dashed var(--md-outline); border-radius:var(--md-shape-lg);" +
            "background:var(--md-surface-container);");
        R(".vmd-empty-title", "margin:0 0 6px; font-size:16px; font-weight:500; color:var(--md-on-surface);");
        R(".vmd-empty-sub", "margin:0; font-size:13px;");

        // --- Responsive ---------------------------------------------------------------------
        Raw("@media (max-width: 700px) {");
        Raw("  " + root + ".vmd-cells, " + root + ".vmd-add-row { padding-left:12px; padding-right:12px; }");
        Raw("}");

        // --- Keyframes (global by name) -----------------------------------------------------
        Raw("@keyframes vmd-run-pulse { 0%,100% { opacity:1; } 50% { opacity:0.35; } }");

        return sb.ToString();
    }

    /// <summary>
    /// Builds the interactive layout script. The layout-root selector is composed from the
    /// extension and layout ids so a single source of truth drives both CSS and JS scoping.
    /// </summary>
    public static string BuildJs(string extensionId, string layoutId)
    {
        var selector = $".verso-layout-root[data-extension-id=\"{extensionId}\"][data-layout-id=\"{layoutId}\"]";
        return JsTemplate.Replace("__SELECTOR__", selector);
    }

    // Classic, deferred script. It binds the host bridge using the ids the host stamps onto the
    // layout root, wires the insert affordances to interactions, and subscribes to host notebook
    // state-change events so the slot set stays in sync when cells are added or removed (from any
    // source) and running cells get a live marker. mount()/teardown() plus a MutationObserver
    // re-bind cleanly whenever the host re-renders the layout root.
    private const string JsTemplate = @"
(function () {
  var SEL = '__SELECTOR__';
  function slice(x) { return Array.prototype.slice.call(x); }

  var root = document.currentScript && document.currentScript.previousElementSibling;
  while (root && !(root.matches && root.matches(SEL))) root = root.previousElementSibling;
  if (!root) root = document.querySelector(SEL);
  if (!root) return;

  // Bind the host bridge via the explicit bind({...}) form using the ids the host stamps onto
  // the layout root. The try/catch keeps a bridge failure from taking down the layout.
  var bridge = null;
  try {
    if (window.verso && window.verso.layout && window.verso.layout.bind) {
      bridge = window.verso.layout.bind({
        extensionId: root.getAttribute('data-extension-id'),
        layoutId: root.getAttribute('data-layout-id'),
        frameInstanceId: root.getAttribute('data-frame-instance-id') || ''
      });
    }
  } catch (e) { bridge = null; }

  // --- Live sync: react to host notebook state-change events. A running cell gets a marker
  // (cheap, no re-render); structural changes coalesce a requestRender() so the slot set
  // re-syncs. Cells added or removed from the cell's own controls, the insert affordances, or
  // the host toolbar all arrive here, so the layout never shows a stale slot. Subscribed once
  // for the layout's lifetime; self-removes if the root is detached. ---
  var runningCells = {};
  // Leading-edge debounce: render immediately on the first change so a single add/remove is
  // instant, then open a short cooldown that coalesces a burst into one follow-up. requestRender
  // regenerates from the current model, so the trailing render after the cooldown settles the
  // slot set to the final state and a missed last-change can't leave it stale.
  var renderTimer = null;
  var renderPending = false;
  function scheduleRender() {
    if (renderTimer) { renderPending = true; return; } // in cooldown: settle once it ends
    if (bridge) bridge.requestRender();                 // leading edge: render now
    renderTimer = setTimeout(function () {
      renderTimer = null;
      if (renderPending) { renderPending = false; if (bridge) bridge.requestRender(); }
    }, 75);
  }
  function markRunning(id, on) {
    if (!id) return;
    var card = root.querySelector('.vmd-cell[data-cell-slot=""' + id + '""]');
    if (card) card.classList.toggle('is-running', !!on);
  }
  var unsubNotebook = null;
  if (bridge && bridge.onNotebookEvent) {
    unsubNotebook = bridge.onNotebookEvent(function (detail) {
      if (!document.contains(root)) { if (unsubNotebook) unsubNotebook(); return; }
      var type = detail && detail.type;
      if (type === 'cell-executing') {
        if (detail.cellId) { runningCells[detail.cellId] = true; markRunning(detail.cellId, true); }
      } else if (type === 'cell-execution-completed') {
        if (detail.cellId) { delete runningCells[detail.cellId]; markRunning(detail.cellId, false); }
      } else if (type === 'notebook-changed') {
        // Structure changed (cell added/removed/moved): re-render so the slots match the cells.
        scheduleRender();
      } else if (type === 'kernel-restarting' || type === 'kernel-restarted') {
        runningCells = {};
      }
    });
  }

  function mount(root) {
    if (root.__vmdTeardown) { try { root.__vmdTeardown(); } catch (e) {} }

    // The layout owns the insert/add actions in its own chrome; each carries data-index and
    // data-type. Skip any [data-action] inside a portaled cell slot so the live cell's own
    // buttons (run, parameter forms, and other extension cell actions) keep their normal
    // handlers instead of being captured here and stopped from bubbling.
    var actionEls = slice(root.querySelectorAll('[data-action]')).filter(function (el) {
      return !el.closest('[data-cell-slot]');
    });
    function onAction(ev) {
      if (!bridge) return;
      ev.preventDefault();
      ev.stopPropagation();
      var action = this.getAttribute('data-action');
      var index = this.getAttribute('data-index') || '';
      var type = this.getAttribute('data-type') || null;
      bridge.interact(action, index, type);
    }
    actionEls.forEach(function (el) { el.addEventListener('click', onAction); });

    // --- Drag to reorder: start from the cell gutter. The reorder is committed through a
    // move-cell interaction, which the layout turns into MoveCellAsync. ---
    var drag = null;
    function slotList() { return slice(root.querySelectorAll('.vmd-cell[data-cell-slot]')); }
    function clearDropMarkers() {
      slotList().forEach(function (c) { c.classList.remove('vmd-drop-before', 'vmd-drop-after'); });
    }
    function onGutterDown(e) {
      if (e.button !== 0) return;
      var gutter = e.target.closest && e.target.closest('.verso-cell-gutter');
      if (!gutter || !root.contains(gutter)) return;
      if (e.target.closest('button')) return; // run/collapse buttons keep their click
      var cell = gutter.closest('.vmd-cell[data-cell-slot]');
      if (!cell) return;
      var fromAttr = cell.getAttribute('data-cell-index');
      drag = {
        cell: cell,
        cellId: cell.getAttribute('data-cell-slot'),
        fromIndex: fromAttr ? parseInt(fromAttr, 10) : 0,
        startY: e.clientY,
        active: false,
        insertBefore: -1
      };
      document.addEventListener('mousemove', onDocMove, true);
      document.addEventListener('mouseup', onDocUp, true);
    }
    function onDocMove(e) {
      if (!drag) return;
      if (!drag.active) {
        if (Math.abs(e.clientY - drag.startY) < 5) return; // movement threshold vs. a plain click
        drag.active = true;
        drag.cell.classList.add('vmd-dragging');
        root.classList.add('vmd-is-dragging');
      }
      e.preventDefault();
      var slots = slotList();
      var pos = slots.length; // visible position for the drop marker
      for (var i = 0; i < slots.length; i++) {
        var r = slots[i].getBoundingClientRect();
        if (e.clientY < r.top + r.height / 2) { pos = i; break; }
      }
      // Map the visible drop position to a full-notebook insert-before index via data-cell-index,
      // so folded (non-contiguous) slots don't skew where the cell lands.
      if (pos >= slots.length) {
        drag.insertBefore = slots.length
          ? parseInt(slots[slots.length - 1].getAttribute('data-cell-index'), 10) + 1
          : 0;
      } else {
        drag.insertBefore = parseInt(slots[pos].getAttribute('data-cell-index'), 10);
      }
      clearDropMarkers();
      if (pos >= slots.length) {
        if (slots.length) slots[slots.length - 1].classList.add('vmd-drop-after');
      } else {
        slots[pos].classList.add('vmd-drop-before');
      }
    }
    function onDocUp() {
      document.removeEventListener('mousemove', onDocMove, true);
      document.removeEventListener('mouseup', onDocUp, true);
      var d = drag; drag = null;
      if (!d) return;
      if (d.active) {
        d.cell.classList.remove('vmd-dragging');
        root.classList.remove('vmd-is-dragging');
        clearDropMarkers();
      }
      if (!d.active || d.insertBefore < 0 || !bridge) return;
      // Translate the insert-before position into MoveCell's final-index semantics (the item is
      // removed first, so a forward move shifts the target down by one).
      var to = (d.fromIndex < d.insertBefore) ? d.insertBefore - 1 : d.insertBefore;
      if (to === d.fromIndex) return;
      bridge.interact('move-cell', String(to), d.cellId);
    }
    root.addEventListener('mousedown', onGutterDown, true);

    root.__vmdTeardown = function () {
      actionEls.forEach(function (el) { el.removeEventListener('click', onAction); });
      root.removeEventListener('mousedown', onGutterDown, true);
      document.removeEventListener('mousemove', onDocMove, true);
      document.removeEventListener('mouseup', onDocUp, true);
    };

    // Re-apply running markers that outlived this re-render.
    Object.keys(runningCells).forEach(function (id) { markRunning(id, true); });
    root.__vmdStage = root.querySelector('.vmd-root');
  }

  mount(root);

  // The host swaps the layout root's children on every requestRender(); re-mount when it does.
  if (window.MutationObserver && !root.__vmdObserver) {
    var obs = new MutationObserver(function () {
      var s = root.querySelector('.vmd-root');
      if (s && s !== root.__vmdStage) mount(root);
    });
    obs.observe(root, { childList: true, subtree: false });
    root.__vmdObserver = obs;
  }
})();
";
}
