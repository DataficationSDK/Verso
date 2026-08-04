using System.Text;

namespace Verso.Showcase.DagNotebook;

/// <summary>
/// The CSS and JavaScript the DAG Notebook layout ships to the host via
/// <c>ILayoutEngine.GetStaticAssetsAsync</c>. The visual base is a faithful copy of the built-in
/// notebook layout's stylesheet and script (same cards, insert rails, and drag-to-reorder), so
/// the layout looks and feels identical to the notebook users already know. Everything DAG
/// specific is additive and namespaced <c>vdag-</c>: the header bar, the dependency badge chips,
/// the stale/failed cell states, and the script's execution-event relay.
///
/// The CSS is fully self-scoped: every selector is prefixed with the layout-root attributes so
/// the styling cannot leak past this layout into the host page or a sibling layout.
/// </summary>
internal static class DagNotebookStyles
{
    /// <summary>
    /// Builds the layout-scoped stylesheet. <paramref name="root"/> is the layout-root selector
    /// prefix (ending with a space).
    /// </summary>
    public static string BuildCss(string root)
    {
        var sb = new StringBuilder(16 * 1024);

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
        // "vdag-" to avoid colliding with the host or another layout.
        void Raw(string css) => sb.Append(css).Append('\n');

        // =====================================================================================
        // Base: copied from the built-in notebook layout so this layout matches it exactly.
        // =====================================================================================

        // --- Root surface + design tokens ---------------------------------------------------
        R(".vmd-root",
            "position:relative; box-sizing:border-box; min-height:100%;" +
            "display:flex; flex-direction:column; gap:0; padding:16px 0 64px;" +
            "font-family:var(--verso-ui-font-family, Roboto, system-ui, -apple-system, 'Segoe UI', sans-serif);" +
            "color:var(--md-on-surface);" +
            "--md-surface:var(--verso-bg-default, var(--verso-editor-background, #fff));" +
            "--md-surface-container:var(--verso-bg-elevated, var(--verso-toolbar-background, #f3f3f3));" +
            "--md-on-surface:var(--verso-fg-default, var(--verso-editor-foreground, #1c1b1f));" +
            "--md-on-surface-variant:var(--verso-fg-muted, var(--verso-editor-line-number, #49454f));" +
            "--md-primary:var(--verso-accent, var(--verso-accent-primary, #6750a4));" +
            "--md-on-primary:var(--md-surface);" +
            "--md-outline:var(--verso-border-default, var(--verso-cell-border, rgba(0,0,0,0.16)));" +
            "--md-outline-variant:color-mix(in srgb, var(--md-outline) 55%, transparent);" +
            "--md-error:var(--verso-status-error, #b3261e);" +
            "--vdag-warn:var(--verso-status-warning, #9a6700);" +
            "--vdag-ok:var(--verso-status-success, #2e7d32);" +
            "--md-elev-1:0 1px 2px rgba(0,0,0,0.18), 0 1px 3px 1px rgba(0,0,0,0.10);" +
            "--md-elev-2:0 2px 4px rgba(0,0,0,0.18), 0 4px 8px 3px rgba(0,0,0,0.12);" +
            "--md-shape-sm:8px; --md-shape-md:12px; --md-shape-lg:16px; --md-shape-full:999px;" +
            "background:var(--md-surface);" +
            "--verso-cell-background:transparent;" +
            "--verso-cell-border:transparent;" +
            "--verso-cell-hover-background:transparent;" +
            "--verso-cell-border-radius:var(--md-shape-md);" +
            "--verso-cell-running-indicator:transparent;" +
            "--verso-cell-output-background:color-mix(in srgb, var(--md-on-surface) 4%, var(--md-surface));");

        // --- Cell list ----------------------------------------------------------------------
        R(".vmd-cells",
            "display:flex; flex-direction:column; gap:0;" +
            "width:100%; padding:0 var(--verso-content-margin-horizontal, 24px);");

        R(".vmd-cell",
            "position:relative; border-radius:var(--md-shape-lg); background:var(--md-surface);" +
            "border:1px solid var(--md-outline-variant); box-shadow:var(--md-elev-1);" +
            "transition:box-shadow 160ms ease, border-color 160ms ease;");
        R(".vmd-cell:hover", "box-shadow:var(--md-elev-2);");
        R(".vmd-cell::before",
            "content:''; position:absolute; left:0; top:10px; bottom:10px; width:3px; opacity:0;" +
            "border-radius:0 3px 3px 0; background:var(--md-primary); transition:opacity 160ms ease;");
        R(".vmd-cell:hover::before, .vmd-cell.is-running::before, .vmd-cell:has(.verso-cell--executing)::before", "opacity:1;");
        R(".vmd-cell.is-running, .vmd-cell:has(.verso-cell--executing)", "border-color:var(--md-primary); box-shadow:var(--md-elev-2);");
        R(".vmd-cell.is-running::before, .vmd-cell:has(.verso-cell--executing)::before", "animation:vdag-run-pulse 1200ms ease-in-out infinite;");
        R(".vmd-cell > *", "padding:4px 6px;");
        R(".vmd-cell[data-cell-slot]:not(:has(> [data-cell-id]))", "display:none;");

        // --- Editor: flatten into the card -------------------------------------------------
        R(".verso-monaco-editor",
            "border-color:transparent; border-radius:var(--md-shape-sm);" +
            "transition:border-color 140ms ease;");
        R(".verso-monaco-editor:hover", "border-color:var(--md-outline-variant);");
        R(".verso-cell--selected .verso-monaco-editor", "border-color:var(--md-outline);");
        R(".monaco-editor .view-overlays .current-line", "border-color:transparent;");

        // --- Always-on type / language badges ----------------------------------------------
        R(".verso-cell-toolbar", "opacity:1; pointer-events:auto;");
        R(".verso-cell-toolbar-actions",
            "opacity:0; pointer-events:none; transition:opacity 140ms ease;");
        R(".verso-cell:hover .verso-cell-toolbar-actions," +
          ".verso-cell--selected .verso-cell-toolbar-actions," +
          ".verso-cell--executing .verso-cell-toolbar-actions",
            "opacity:1; pointer-events:auto;");

        // --- Rendered markdown: chromeless, reads as document prose -------------------------
        R(".vmd-cell:has(.verso-cell--preview)",
            "background:transparent; border-color:transparent; box-shadow:none;");
        R(".vmd-cell:has(.verso-cell--preview):hover",
            "background:var(--md-surface); border-color:var(--md-outline-variant); box-shadow:var(--md-elev-1);");
        R(".verso-cell--preview .verso-cell-toolbar", "opacity:0; pointer-events:none;");
        R(".verso-cell--preview:hover .verso-cell-toolbar," +
          ".verso-cell--preview:hover .verso-cell-toolbar-actions",
            "opacity:1; pointer-events:auto;");

        // --- Drag to reorder ----------------------------------------------------------------
        R(".vmd-cell .verso-cell-gutter", "cursor:grab;");
        R(".vmd-root.vmd-is-dragging", "cursor:grabbing; user-select:none;");
        R(".vmd-cell.vmd-dragging", "opacity:0.5;");
        R(".vmd-cell.vmd-drop-before", "box-shadow:0 -3px 0 0 var(--md-primary), var(--md-elev-1);");
        R(".vmd-cell.vmd-drop-after", "box-shadow:0 3px 0 0 var(--md-primary), var(--md-elev-1);");

        // --- Insert rail (between cells) ----------------------------------------------------
        R(".vmd-insert",
            "display:flex; align-items:center; justify-content:center; height:8px;" +
            "position:relative; transition:height 140ms ease;");
        R(".vmd-insert::after",
            "content:''; position:absolute; left:0; right:0; top:-4px; bottom:-4px;");
        R(".vmd-insert-buttons",
            "position:relative; z-index:1; display:inline-flex; gap:6px; opacity:0;" +
            "pointer-events:none; transition:opacity 140ms ease;");
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

        // =====================================================================================
        // DAG additions. Everything below is layered on top of the base look.
        // =====================================================================================

        // --- Header bar: layout identity, Run DAG, and the auto-run toggle -------------------
        R(".vdag-header",
            "display:flex; align-items:center; justify-content:space-between; gap:12px; flex-wrap:wrap;" +
            "margin:0 var(--verso-content-margin-horizontal, 24px) 14px; padding:8px 14px;" +
            "border:1px solid var(--md-outline-variant); border-radius:var(--md-shape-lg);" +
            "background:var(--md-surface-container);");
        R(".vdag-title",
            "display:flex; align-items:center; gap:8px; font-size:13px; font-weight:600;" +
            "color:var(--md-on-surface);");
        R(".vdag-logo", "color:var(--md-primary); font-size:15px; line-height:1;");
        R(".vdag-controls", "display:flex; align-items:center; gap:8px; flex-wrap:wrap;");
        R(".vdag-btn",
            "cursor:pointer; display:inline-flex; align-items:center; gap:6px; padding:4px 14px;" +
            "border-radius:var(--md-shape-full); font-family:inherit; font-size:12px; font-weight:500;" +
            "border:1px solid var(--md-outline); color:var(--md-on-surface); background:var(--md-surface);" +
            "transition:background 140ms ease, border-color 140ms ease;");
        R(".vdag-btn:hover",
            "border-color:var(--md-primary);" +
            "background:color-mix(in srgb, var(--md-primary) 10%, var(--md-surface));");
        R(".vdag-btn--primary",
            "background:var(--md-primary); border-color:var(--md-primary); color:var(--md-on-primary);");
        R(".vdag-btn--primary:hover",
            "background:color-mix(in srgb, var(--md-on-surface) 15%, var(--md-primary));");
        R(".vdag-dot",
            "width:8px; height:8px; border-radius:50%; background:var(--md-outline);" +
            "transition:background 140ms ease;");
        R(".vdag-toggle.is-on .vdag-dot", "background:var(--vdag-ok);");
        R(".vdag-toggle.is-off", "opacity:0.7;");

        // --- Dependency badges: the chip row above each linked cell --------------------------
        // A unit wraps the badge row and the cell slot so the chips survive the portal moving
        // the live cell into the slot.
        R(".vdag-unit", "display:flex; flex-direction:column;");
        R(".vdag-badges",
            "display:flex; flex-wrap:wrap; gap:4px; justify-content:flex-end;" +
            "padding:0 10px; margin:0 0 3px;");
        R(".vdag-chip",
            "display:inline-flex; align-items:center; gap:4px; padding:1px 8px;" +
            "border-radius:var(--md-shape-full); font-size:11px; line-height:16px;" +
            "font-family:var(--verso-mono-font-family, ui-monospace, 'SF Mono', Consolas, monospace);" +
            "border:1px solid var(--md-outline-variant); background:var(--md-surface-container);" +
            "color:var(--md-on-surface-variant);" +
            "transition:border-color 140ms ease, color 140ms ease, background 140ms ease;");
        R("button.vdag-chip", "cursor:pointer;");
        R("button.vdag-chip:hover", "border-color:var(--md-primary); color:var(--md-primary);");
        R(".vdag-arrow", "font-size:10px; line-height:1;");
        R(".vdag-chip--in .vdag-arrow", "color:var(--md-primary);");
        R(".vdag-chip--out .vdag-arrow", "color:var(--vdag-ok);");
        R(".vdag-var", "font-weight:600;");
        R(".vdag-chip--warn",
            "cursor:default; color:var(--vdag-warn);" +
            "border-color:color-mix(in srgb, var(--vdag-warn) 45%, transparent);" +
            "background:color-mix(in srgb, var(--vdag-warn) 10%, var(--md-surface));");
        // Live: this cell's variable follows a control rather than a computation, so a reader can
        // tell at a glance which value moves without anything being run.
        R(".vdag-chip--live",
            "cursor:default; color:var(--md-primary);" +
            "border-color:color-mix(in srgb, var(--md-primary) 45%, transparent);" +
            "background:color-mix(in srgb, var(--md-primary) 10%, var(--md-surface));");
        R(".vdag-chip--live .vdag-arrow", "color:var(--md-primary); font-size:11px;");

        // --- Cell dependency states -----------------------------------------------------------
        // Stale: an upstream producer ran more recently than this cell; dashed warning border.
        R(".vdag-unit.vdag-stale > .vmd-cell",
            "border-style:dashed;" +
            "border-color:color-mix(in srgb, var(--vdag-warn) 65%, transparent);");
        // Failed: the most recent execution of this cell errored.
        R(".vdag-unit.vdag-failed > .vmd-cell", "border-color:var(--md-error);");
        // Flash: brief highlight after navigating to a cell from a badge chip.
        R(".vdag-unit.vdag-flash > .vmd-cell",
            "border-color:var(--md-primary);" +
            "box-shadow:0 0 0 3px color-mix(in srgb, var(--md-primary) 30%, transparent), var(--md-elev-2);");

        // --- Responsive ---------------------------------------------------------------------
        Raw("@media (max-width: 700px) {");
        Raw("  " + root + ".vmd-cells, " + root + ".vmd-add-row { padding-left:12px; padding-right:12px; }");
        Raw("  " + root + ".vdag-header { margin-left:12px; margin-right:12px; }");
        Raw("}");

        // --- Keyframes (global by name) -----------------------------------------------------
        Raw("@keyframes vdag-run-pulse { 0%,100% { opacity:1; } 50% { opacity:0.35; } }");

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

    // Classic, deferred script. The base behavior is copied from the built-in notebook layout:
    // bind the host bridge, wire the insert affordances, drag-to-reorder, and re-render on
    // structural notebook changes. The DAG additions relay cell execution events to the layout's
    // interaction handler (which drives the server-side cascade), maintain client-side stale
    // markers from the dependency graph the layout embeds as JSON, and make badge chips scroll
    // to their linked cell.
    private const string JsTemplate = @"
(function () {
  var SEL = '__SELECTOR__';
  function slice(x) { return Array.prototype.slice.call(x); }

  var root = document.currentScript && document.currentScript.previousElementSibling;
  while (root && !(root.matches && root.matches(SEL))) root = root.previousElementSibling;
  if (!root) root = document.querySelector(SEL);
  if (!root) return;

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

  // --- DAG graph + stale tracking -----------------------------------------------------------
  // The layout embeds its dependency graph as JSON on every render. Stale markers live here in
  // the script (not the rendered HTML) so they survive re-renders and stay purely client-side:
  // a cell goes stale the moment an upstream producer starts running and recovers the moment it
  // runs itself.
  var graph = { cells: {} };
  var staleCells = {};
  function readGraph() {
    var el = root.querySelector('script[data-vdag-graph]');
    if (!el) { graph = { cells: {} }; return; }
    try { graph = JSON.parse(el.textContent || '{}') || { cells: {} }; }
    catch (e) { graph = { cells: {} }; }
    if (!graph.cells) graph.cells = {};
    // Cells a control left behind. Nothing ran to announce these, so they arrive with the graph
    // and join the marks already held here rather than replacing them.
    (graph.stale || []).forEach(function (id) { staleCells[id] = true; });
  }
  function downstreamOf(id) {
    var seen = {}, stack = [id];
    while (stack.length) {
      var node = graph.cells[stack.pop()];
      if (!node) continue;
      (node.dependents || []).forEach(function (d) {
        if (!seen[d]) { seen[d] = true; stack.push(d); }
      });
    }
    delete seen[id];
    return Object.keys(seen);
  }
  function unitOf(id) {
    return root.querySelector('.vdag-unit[data-dag-cell=""' + id + '""]');
  }
  function markStale(id, on) {
    if (on) staleCells[id] = true; else delete staleCells[id];
    var unit = unitOf(id);
    if (unit) unit.classList.toggle('vdag-stale', !!on);
  }

  // --- Live sync (copied from the notebook layout) -------------------------------------------
  var runningCells = {};
  var renderTimer = null;
  var renderPending = false;
  function scheduleRender() {
    if (renderTimer) { renderPending = true; return; }
    if (bridge) bridge.requestRender();
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
  // --- Controls ------------------------------------------------------------------------------
  // A widget trait shared as a notebook variable changes the store with no cell having run, which
  // is the only way the store changes while nothing is running. The layout is the one that knows
  // which variable moved, so this relays the fact and lets it decide; the coalescing window keeps
  // a dragged control from sending one message per step, and a run in progress is skipped because
  // the cell doing the writing already announces itself through the execution events.
  var controlTimer = null;
  var controlPending = false;
  function anyCellRunning() {
    for (var id in runningCells) { if (runningCells[id]) return true; }
    return false;
  }
  function relayControlChange() {
    if (!bridge || !graph.hasControls) return;
    // A cell that is running writes variables of its own and announces itself through the
    // execution events, so its writes are not relayed. The change is held rather than dropped:
    // a control moved while something was running still gets acted on when the run ends, and a
    // completion that never arrives costs one late reaction rather than every later one.
    if (anyCellRunning()) { controlPending = true; return; }
    controlPending = false;
    if (controlTimer) clearTimeout(controlTimer);
    controlTimer = setTimeout(function () {
      controlTimer = null;
      try { bridge.interact('dag-variables-changed', ''); } catch (e) {}
    }, 120);
  }

  var unsubNotebook = null;
  if (bridge && bridge.onNotebookEvent) {
    unsubNotebook = bridge.onNotebookEvent(function (detail) {
      if (!document.contains(root)) { if (unsubNotebook) unsubNotebook(); return; }
      var type = detail && detail.type;
      if (type === 'cell-executing') {
        if (detail.cellId) {
          runningCells[detail.cellId] = true;
          markRunning(detail.cellId, true);
          // The cell is producing fresh values, so it is no longer stale itself, and
          // everything downstream of it is stale until it re-runs.
          markStale(detail.cellId, false);
          downstreamOf(detail.cellId).forEach(function (d) { markStale(d, true); });
          // Tell the layout so it can cancel a pending cascade when a new run begins.
          if (bridge) bridge.interact('dag-cell-executing', detail.cellId);
        }
      } else if (type === 'cell-execution-completed') {
        if (detail.cellId) {
          delete runningCells[detail.cellId];
          markRunning(detail.cellId, false);
          markStale(detail.cellId, false);
          // The layout decides whether this completion triggers a cascade.
          if (bridge) bridge.interact('dag-cell-completed', detail.cellId);
          // A control moved while this was running has been waiting for it to finish.
          if (controlPending) relayControlChange();
        }
      } else if (type === 'variables-changed') {
        relayControlChange();
      } else if (type === 'notebook-changed') {
        scheduleRender();
      } else if (type === 'kernel-restarting' || type === 'kernel-restarted') {
        runningCells = {};
      }
    });
  }

  function mount(root) {
    if (root.__vdagTeardown) { try { root.__vdagTeardown(); } catch (e) {} }

    readGraph();

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

    // Badge chips navigate to the linked cell.
    function onChipClick(ev) {
      var chip = ev.target.closest && ev.target.closest('.vdag-chip[data-goto]');
      if (!chip || !root.contains(chip)) return;
      ev.preventDefault();
      ev.stopPropagation();
      var unit = unitOf(chip.getAttribute('data-goto'));
      if (!unit) return;
      unit.scrollIntoView({ behavior: 'smooth', block: 'center' });
      unit.classList.add('vdag-flash');
      setTimeout(function () { unit.classList.remove('vdag-flash'); }, 1400);
    }
    root.addEventListener('click', onChipClick, true);

    // --- Drag to reorder (copied from the notebook layout) ---
    var drag = null;
    function slotList() { return slice(root.querySelectorAll('.vmd-cell[data-cell-slot]')); }
    function clearDropMarkers() {
      slotList().forEach(function (c) { c.classList.remove('vmd-drop-before', 'vmd-drop-after'); });
    }
    function onGutterDown(e) {
      if (e.button !== 0) return;
      var gutter = e.target.closest && e.target.closest('.verso-cell-gutter');
      if (!gutter || !root.contains(gutter)) return;
      if (e.target.closest('button')) return;
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
        if (Math.abs(e.clientY - drag.startY) < 5) return;
        drag.active = true;
        drag.cell.classList.add('vmd-dragging');
        root.classList.add('vmd-is-dragging');
      }
      e.preventDefault();
      var slots = slotList();
      var pos = slots.length;
      for (var i = 0; i < slots.length; i++) {
        var r = slots[i].getBoundingClientRect();
        if (e.clientY < r.top + r.height / 2) { pos = i; break; }
      }
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
      var to = (d.fromIndex < d.insertBefore) ? d.insertBefore - 1 : d.insertBefore;
      if (to === d.fromIndex) return;
      bridge.interact('move-cell', String(to), d.cellId);
    }
    root.addEventListener('mousedown', onGutterDown, true);

    root.__vdagTeardown = function () {
      actionEls.forEach(function (el) { el.removeEventListener('click', onAction); });
      root.removeEventListener('click', onChipClick, true);
      root.removeEventListener('mousedown', onGutterDown, true);
      document.removeEventListener('mousemove', onDocMove, true);
      document.removeEventListener('mouseup', onDocUp, true);
    };

    // Re-apply markers that outlived this re-render.
    Object.keys(runningCells).forEach(function (id) { markRunning(id, true); });
    Object.keys(staleCells).forEach(function (id) { markStale(id, true); });
    root.__vdagStage = root.querySelector('.vmd-root');
  }

  mount(root);

  // The host swaps the layout root's children on every requestRender(); re-mount when it does.
  if (window.MutationObserver && !root.__vdagObserver) {
    var obs = new MutationObserver(function () {
      var s = root.querySelector('.vmd-root');
      if (s && s !== root.__vdagStage) mount(root);
    });
    obs.observe(root, { childList: true, subtree: false });
    root.__vdagObserver = obs;
  }
})();
";
}
