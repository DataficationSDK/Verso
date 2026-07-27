using System.Text;

namespace Verso.Showcase.SlideStudio;

/// <summary>
/// The CSS and JavaScript Slide Studio ships to the host via
/// <c>ILayoutEngine.GetStaticAssetsAsync</c>. Everything is namespaced <c>vss-</c> and the
/// CSS is fully self-scoped: every selector is prefixed with the layout-root attributes so
/// the styling cannot leak past this layout into the host page or a sibling layout.
///
/// The script keeps its responsibilities client-side so they cost no server round trips:
/// the splitter drag (one interaction on release, like the dashboard layout), presenter
/// navigation (the deck is prerendered hidden; arrows switch the current slide and hand
/// the active cell between the editor slot and its slide), and optimistic checkbox
/// feedback. Flag toggles go through the cell interaction channel so the notebook is
/// marked dirty; a debounced re-render then keeps the filmstrip, panes, and prerendered
/// deck consistent.
/// </summary>
internal static class SlideStudioStyles
{
    /// <summary>
    /// Builds the layout-scoped stylesheet. <paramref name="root"/> is the layout-root
    /// selector prefix (ending with a space).
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

        void Raw(string css) => sb.Append(css).Append('\n');

        // --- Root: a viewport-filling studio surface -----------------------------------------
        R(".vss-root",
            "display:flex; flex-direction:column; gap:10px; box-sizing:border-box;" +
            "padding:12px 16px 16px; height:calc(100vh - 140px); min-height:480px;" +
            "font-family:var(--verso-font-family-sans, system-ui, -apple-system, 'Segoe UI', sans-serif);" +
            "font-size:var(--verso-font-size-base, 13px);" +
            "color:var(--verso-fg-default, #1c1b1f);");

        // --- Toolbar --------------------------------------------------------------------------
        R(".vss-toolbar",
            "display:flex; align-items:center; justify-content:space-between; gap:12px; flex:none;" +
            "padding:8px 14px; border:1px solid var(--verso-border-default, rgba(0,0,0,0.16));" +
            "border-radius:10px; background:var(--verso-bg-elevated, #f3f3f3);");
        R(".vss-title",
            "display:flex; align-items:center; gap:8px; font-size:13px; font-weight:600;");
        R(".vss-logo", "color:var(--verso-accent, #6750a4); font-size:12px; line-height:1;");
        R(".vss-toolbar-right", "display:flex; align-items:center; gap:12px;");
        R(".vss-deck-count", "font-size:12px; color:var(--verso-fg-muted, #49454f);");
        R(".vss-btn",
            "cursor:pointer; padding:5px 16px; border-radius:999px; font-family:inherit;" +
            "font-size:12px; font-weight:500; border:1px solid var(--verso-border-default, rgba(0,0,0,0.16));" +
            "background:var(--verso-bg-default, #fff); color:var(--verso-fg-default, #1c1b1f);" +
            "transition:background 140ms ease, border-color 140ms ease;");
        R(".vss-btn--primary",
            "background:var(--verso-accent, #6750a4); border-color:var(--verso-accent, #6750a4);" +
            "color:var(--verso-bg-default, #fff);");
        R(".vss-btn--primary:hover:not([disabled])", "filter:brightness(1.12);");
        R(".vss-btn[disabled]", "opacity:0.5; cursor:default;");

        // --- Body: filmstrip + workspace --------------------------------------------------------
        R(".vss-body", "display:flex; gap:12px; flex:1; min-height:0;");

        // --- Filmstrip ---------------------------------------------------------------------------
        R(".vss-filmstrip",
            "width:190px; flex:none; overflow-y:auto; overflow-x:hidden;" +
            "display:flex; flex-direction:column; gap:10px; padding:2px;");
        R(".vss-tile",
            "position:relative; flex:none; cursor:pointer; padding:6px 8px 4px;" +
            "border:1px solid var(--verso-border-default, rgba(0,0,0,0.16)); border-radius:8px;" +
            "background:var(--verso-bg-elevated, #f3f3f3);" +
            "transition:border-color 140ms ease, box-shadow 140ms ease, opacity 140ms ease;");
        R(".vss-tile:hover", "border-color:var(--verso-accent, #6750a4);");
        R(".vss-tile.is-active",
            "border-color:var(--verso-accent, #6750a4);" +
            "box-shadow:0 0 0 1px var(--verso-accent, #6750a4);");
        R(".vss-tile.is-excluded .vss-tile-preview, .vss-tile.is-excluded .vss-tile-caption, " +
          ".vss-tile.is-excluded .vss-tile-index",
            "opacity:0.4;");
        R(".vss-tile-include",
            "position:absolute; top:4px; left:7px; z-index:2; display:inline-flex; cursor:pointer;");
        R(".vss-tile-index",
            "position:absolute; top:3px; right:9px; font-size:10px;" +
            "color:var(--verso-fg-muted, #49454f); font-variant-numeric:tabular-nums;");
        R(".vss-tile-preview",
            "margin-top:16px; height:74px; overflow:hidden; border-radius:4px;" +
            "border:1px solid var(--verso-border-default, rgba(0,0,0,0.16));" +
            "background:var(--verso-bg-default, #fff);");
        // The preview box renders the real output HTML at 4x tile width scaled to a quarter,
        // so thumbnails look like the actual output. Pointer events are off: a tile is one
        // click target, never an interactive copy of the output.
        R(".vss-tile-scale",
            "width:400%; transform:scale(0.25); transform-origin:top left; pointer-events:none;");
        R(".vss-tile-scale .vss-out", "padding:10px;");
        R(".vss-tile-scale .vss-out--text pre, .vss-tile-scale .vss-out--error pre",
            "font-size:30px; line-height:1.4;");
        R(".vss-tile-source",
            "margin:0; padding:10px; font-family:var(--verso-font-family-mono, ui-monospace, Consolas, monospace);" +
            "font-size:30px; line-height:1.4; white-space:pre-wrap; word-break:break-word;" +
            "color:var(--verso-fg-muted, #49454f);");
        // Stand-in for script-driven outputs, which are never copied into a tile. Sized
        // for the 4x-wide, quarter-scaled preview box like the source excerpt above.
        R(".vss-tile-interactive",
            "display:flex; align-items:center; justify-content:center; height:280px;" +
            "font-size:30px; font-style:italic; color:var(--verso-fg-muted, #49454f);");
        R(".vss-tile-caption",
            "padding:3px 2px 1px; font-size:10px; color:var(--verso-fg-muted, #49454f);" +
            "white-space:nowrap; overflow:hidden; text-overflow:ellipsis;");
        R(".vss-empty",
            "padding:20px 12px; font-size:12px; color:var(--verso-fg-muted, #49454f);" +
            "border:1px dashed var(--verso-border-default, rgba(0,0,0,0.16)); border-radius:8px;");

        // --- Workspace: editor | splitter | output ------------------------------------------------
        // The --vss-split custom property (inline on .vss-main, rewritten live by the
        // splitter drag) drives both the grid template and the output overlay's left
        // edge below, so the overlay always sits exactly over the output pane.
        R(".vss-main",
            "position:relative; display:grid; grid-template-rows:1fr;" +
            "grid-template-columns:calc((100% - 6px) * var(--vss-split, 0.55)) 6px minmax(0, 1fr);" +
            "flex:1; min-width:0; min-height:0;");
        R(".vss-pane",
            "display:flex; flex-direction:column; min-width:0; min-height:0; overflow:hidden;" +
            "border:1px solid var(--verso-border-default, rgba(0,0,0,0.16)); border-radius:8px;" +
            "background:var(--verso-bg-default, #fff);");
        // Fixed header height: the output overlay's top offset below assumes it.
        R(".vss-pane-header",
            "display:flex; align-items:center; justify-content:space-between; gap:8px; flex:none;" +
            "height:32px; box-sizing:border-box; padding:0 10px; font-size:12px; overflow:hidden;" +
            "border-bottom:1px solid var(--verso-border-default, rgba(0,0,0,0.16));" +
            "background:var(--verso-bg-elevated, #f3f3f3);");
        R(".vss-pane-name", "font-weight:600;");
        R(".vss-pane-check",
            "display:inline-flex; align-items:center; gap:5px; cursor:pointer; white-space:nowrap;" +
            "font-size:12px; color:var(--verso-fg-muted, #49454f); user-select:none;");
        R(".vss-tile-include input, .vss-pane-check input",
            "accent-color:var(--verso-accent, #6750a4); cursor:pointer; margin:0;");
        R(".vss-editor-slot",
            "flex:1; min-height:0; overflow:auto; padding:8px; display:flex; flex-direction:column;");
        R(".vss-output-body", "flex:1; min-height:0; overflow:auto; padding:12px;");
        R(".vss-pane-empty",
            "padding:24px 16px; font-size:13px; color:var(--verso-fg-muted, #49454f);");

        // The portaled live cell keeps its editor, toolbar, and gutter (the gutter holds
        // the run and stop buttons). Its rendered output section is neither hidden nor
        // copied: this rule lifts the real output element out of the editor column and
        // lays it over the output pane's body. Showing the live element is what makes
        // script-driven outputs (chart libraries and other rich HTML) work, since layout
        // HTML is injected via innerHTML and any scripts in a serialized copy would
        // never execute; it also keeps output element ids unique in the document.
        // Geometry: .vss-main is the positioned ancestor; the left edge repeats the grid
        // template's column calc, 33px clears the 32px pane header plus the pane's top
        // border, and the 1px insets keep the overlay inside the pane's rounded border.
        // Preview-mode cells are the exception: a rendered markdown cell mounts no
        // editor at all and its clickable rendered output IS the way into edit mode, so
        // it stays inline in the editor column until the cell is selected for editing.
        R(".vss-editor-slot .verso-cell:not(.verso-cell--preview) .verso-cell-outputs",
            "position:absolute; top:33px; right:1px; bottom:1px;" +
            "left:calc((100% - 6px) * var(--vss-split, 0.55) + 7px);" +
            "margin:0; padding:12px; overflow:auto; z-index:1; border-radius:0 0 7px 7px;" +
            "background:var(--verso-bg-default, #fff);");
        R(".vss-editor-slot .verso-cell:not(.verso-cell--preview) .verso-cell-output-summary",
            "display:none;");

        // Keep the overlay's containing block at .vss-main. A host may position the
        // selected cell to lift it and its editor popups above the cells that follow it
        // in a list, which is invisible in a notebook but would make the cell itself the
        // containing block here and pull the overlay back into the editor column. One
        // cell occupies this pane, so there is nothing for it to paint above, and the
        // editor keeps its own stacking either way.
        R(".vss-editor-slot .verso-cell--selected", "position:static; z-index:auto;");

        // Fill the pane: stretch the portal wrapper, the cell row, and its content column,
        // then let the editor take the leftover height. Monaco runs with automaticLayout,
        // so overriding the interop's content-based inline height with a flexed box is
        // safe; the editor tracks the container through splitter drags and pane resizes.
        R(".vss-editor-slot > [data-cell-id]",
            "flex:1; min-height:0; display:flex; flex-direction:column;");
        R(".vss-editor-slot .verso-cell", "flex:1; min-height:0; margin-bottom:0;");
        R(".vss-editor-slot .verso-cell-content",
            "display:flex; flex-direction:column; min-height:0;");
        R(".vss-editor-slot .verso-cell-editor",
            "flex:1; min-height:0; display:flex; flex-direction:column; padding-bottom:8px;");
        R(".vss-editor-slot .verso-cell-editor .verso-monaco-editor",
            "flex:1; height:auto !important; min-height:120px;");

        // --- Splitter -------------------------------------------------------------------------
        R(".vss-splitter", "cursor:col-resize; position:relative;");
        R(".vss-splitter::after",
            "content:''; position:absolute; top:0; bottom:0; left:2px; width:2px; border-radius:2px;" +
            "background:var(--verso-border-default, rgba(0,0,0,0.16)); transition:background 140ms ease;");
        R(".vss-splitter:hover::after", "background:var(--verso-accent, #6750a4);");
        R(".vss-resizing", "cursor:col-resize; user-select:none;");

        // --- Output blocks (shared by pane, tiles, and slides) -------------------------------------
        R(".vss-out", "margin:0 0 10px;");
        R(".vss-out:last-child", "margin-bottom:0;");
        R(".vss-out--text pre, .vss-out--error pre",
            "margin:0; white-space:pre-wrap; word-break:break-word;" +
            "font-family:var(--verso-font-family-mono, ui-monospace, Consolas, monospace);" +
            "font-size:var(--verso-font-size-base, 13px);");
        R(".vss-out--error", "color:var(--verso-status-error, #b3261e);");
        R(".vss-out--image img", "max-width:100%; height:auto;");
        // A mermaid copy is diagram source the host renders client-side after injection.
        // Hide the source until then; visibility (not display) keeps the box, which is
        // what tells the deferred renderer the node is ready to measure.
        R(".vss-out--mermaid .mermaid:not([data-processed])", "visibility:hidden;");
        R(".vss-out--mermaid pre.mermaid", "margin:0; text-align:center;");

        // --- Presenter view ---------------------------------------------------------------------
        R(".vss-presenter",
            "position:fixed; inset:0; z-index:1000; display:flex; flex-direction:column;" +
            "background:var(--verso-bg-default, #fff); color:var(--verso-fg-default, #1c1b1f);");
        R(".vss-presenter[hidden]", "display:none;");
        R(".vss-slides", "flex:1; min-height:0; position:relative;");
        R(".vss-slide", "display:none;");
        R(".vss-slide.is-current",
            "display:flex; align-items:center; justify-content:center;" +
            "position:absolute; inset:0; overflow:hidden; padding:32px 48px; box-sizing:border-box;");
        // The fixed-width design canvas. The script measures it and applies a scale
        // transform to fit the viewport (transforms are visual only, so the flex parent
        // centers the unscaled box and the scaled rendering stays centered with it).
        R(".vss-slide-fit",
            "width:960px; max-width:100%; display:flex; flex-direction:column; gap:24px;" +
            "transform-origin:center center;");
        R(".vss-slide-source, .vss-slide-live", "width:100%;");
        R(".vss-slide-source pre",
            "margin:0; padding:18px 22px; overflow:auto; white-space:pre-wrap;" +
            "font-family:var(--verso-font-family-mono, ui-monospace, Consolas, monospace);" +
            "font-size:15px; line-height:1.55; border-radius:10px;" +
            "border:1px solid var(--verso-border-default, rgba(0,0,0,0.16));" +
            "background:var(--verso-bg-elevated, #f3f3f3);");
        // A slide's output area hosts the live cell through a portal slot. Strip the
        // editing chrome the same way the built-in presentation layout does, so only the
        // rendered outputs remain on the slide.
        R(".vss-slide-live", "font-size:16px;");
        R(".vss-slide-live .verso-cell-gutter, .vss-slide-live .verso-cell-toolbar, " +
          ".vss-slide-live .verso-cell-editor, .vss-slide-live .verso-cell-source-preview, " +
          ".vss-slide-live .verso-cell-status, .vss-slide-live .verso-cell-output-summary",
            "display:none;");
        R(".vss-slide-live .verso-cell",
            "border:none; background:transparent; box-shadow:none; margin:0;");
        R(".vss-slide-live .verso-cell-outputs", "padding:0;");
        // Center output content on the canvas. Flex centering (rather than a fit-content
        // width on the block) matters: intrinsic-size content (a chart image, an SVG, a
        // table) centers at its natural width, while fluid content that asks for 100%
        // width still resolves against the full canvas instead of collapsing to zero.
        R(".vss-slide-live .verso-output",
            "display:flex; flex-direction:column; align-items:center;");
        R(".vss-slide-live .verso-output > *", "max-width:100%;");
        // The mermaid pre needs an explicit width: as a centered flex item it would
        // shrink-to-fit, and the rendered SVG's 100% width cannot resolve against
        // that (it falls back to the replaced-element default). Full width lets the
        // SVG fill the canvas up to its natural size; narrower diagrams center.
        R(".vss-slide-live pre.mermaid", "width:100%; margin:0; text-align:center;");
        R(".vss-slide-live .verso-output--text pre",
            "font-size:15px; line-height:1.55; margin:0;");
        // The slide's fallback copy: hidden while the live cell contributes an output
        // element, shown (and the empty slot collapsed) when it does not. A markdown
        // cell being edited and a collapsed output section are the two such states;
        // the live cell renders no output element in either.
        R(".vss-slide-copy", "display:none; width:100%; font-size:16px;");
        R(".vss-slide-live:not(:has(.verso-cell-outputs))", "display:none;");
        R(".vss-slide-live:not(:has(.verso-cell-outputs)) + .vss-slide-copy", "display:block;");
        R(".vss-slide-copy .vss-out",
            "width:fit-content; max-width:100%; margin-left:auto; margin-right:auto;");
        // Not for mermaid copies: the rendered SVG asks for 100% width, which collapses
        // inside a fit-content box. Fluid width; the copy centers its own content.
        R(".vss-slide-copy .vss-out--mermaid", "width:100%;");
        R(".vss-slide-copy .vss-out--text pre, .vss-slide-copy .vss-out--error pre",
            "font-size:15px; line-height:1.55;");
        R(".vss-presenter-hud",
            "position:absolute; bottom:20px; left:50%; transform:translateX(-50%); z-index:1;" +
            "display:flex; align-items:center; gap:8px; padding:7px 14px; border-radius:999px;" +
            "border:1px solid var(--verso-border-default, rgba(0,0,0,0.16));" +
            "background:var(--verso-bg-elevated, #f3f3f3);" +
            "opacity:0.35; transition:opacity 140ms ease;");
        R(".vss-presenter-hud:hover", "opacity:1;");
        R(".vss-hud-btn",
            "cursor:pointer; border:none; background:transparent; padding:4px 8px;" +
            "font-size:13px; line-height:1; color:var(--verso-fg-default, #1c1b1f);");
        R(".vss-hud-btn:hover", "color:var(--verso-accent, #6750a4);");
        R(".vss-slide-counter",
            "min-width:4em; text-align:center; font-size:13px; font-variant-numeric:tabular-nums;");

        // --- Responsive -----------------------------------------------------------------------
        Raw("@media (max-width: 900px) {");
        Raw("  " + root + ".vss-filmstrip { width:130px; }");
        Raw("  " + root + ".vss-slide.is-current { padding:24px; }");
        Raw("}");

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

    // Classic, deferred script. Stage-scoped listeners are rebound by the MutationObserver
    // whenever the host swaps the layout HTML on a re-render; document-level listeners are
    // attached once and guard on the presenting flag.
    private const string JsTemplate = @"
(function () {
  var SEL = '__SELECTOR__';
  function slice(x) { return Array.prototype.slice.call(x); }

  var root = document.currentScript && document.currentScript.previousElementSibling;
  while (root && !(root.matches && root.matches(SEL))) root = root.previousElementSibling;
  if (!root) root = document.querySelector(SEL);
  if (!root) return;

  var EXT_ID = root.getAttribute('data-extension-id');

  var bridge = null;
  try {
    if (window.verso && window.verso.layout && window.verso.layout.bind) {
      bridge = window.verso.layout.bind({
        extensionId: EXT_ID,
        layoutId: root.getAttribute('data-layout-id'),
        frameInstanceId: root.getAttribute('data-frame-instance-id') || ''
      });
    }
  } catch (e) { bridge = null; }

  // --- Re-render plumbing ---------------------------------------------------------------
  // Debounced, and deferred entirely while presenting: a re-render swaps the DOM the
  // presenter overlay lives in (and drops fullscreen with it), so updates wait for exit.
  var renderTimer = null;
  var renderPending = false;
  var renderDeferred = false;
  function scheduleRender() {
    if (presenting) { renderDeferred = true; return; }
    if (renderTimer) { renderPending = true; return; }
    if (bridge) bridge.requestRender();
    renderTimer = setTimeout(function () {
      renderTimer = null;
      if (renderPending) { renderPending = false; if (bridge) bridge.requestRender(); }
    }, 75);
  }

  // Flag toggles ride the cell interaction channel (not layout/interact) so the host
  // marks the notebook dirty; the layout's ICellInteractionHandler persists the flag.
  function setPresenterFlag(cellId, flag, value) {
    if (!window.versoCellInteract || typeof window.versoCellInteract.cellInteract !== 'function') {
      return Promise.resolve(null);
    }
    try {
      return Promise.resolve(window.versoCellInteract.cellInteract(
        cellId, EXT_ID, 'set-presenter-flag',
        JSON.stringify({ flag: flag, value: !!value }), null));
    } catch (e) { return Promise.resolve(null); }
  }

  // --- Presenter state (script-scoped so it survives re-renders) --------------------------
  var presenting = false;
  var slideIndex = 0;

  function slides() { return slice(root.querySelectorAll('.vss-slide')); }

  // Scale the slide's fixed-width canvas to fit the viewport: up for small content
  // (capped, so text does not balloon), down for content taller than the screen.
  // Geometry only; nothing re-executes.
  function fitSlide(slide) {
    var fit = slide && slide.querySelector('.vss-slide-fit');
    if (!fit) return;
    fit.style.transform = 'none';
    var cs = getComputedStyle(slide);
    var availW = slide.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
    var availH = slide.clientHeight - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom);
    var rect = fit.getBoundingClientRect();
    if (rect.width < 1 || rect.height < 1 || availW < 1 || availH < 1) return;
    var scale = Math.min(availW / rect.width, availH / rect.height, 2.2);
    fit.style.transform = 'scale(' + scale.toFixed(3) + ')';
  }

  // --- Live slide handoff -------------------------------------------------------------
  // Slides host the live cell through a real [data-cell-slot]. The host's portal fills
  // those slots for every cell except the active one: the workspace's editor slot comes
  // first in document order and wins that cell. While its slide is current, borrow the
  // active cell's node from the editor slot and return it on navigation or exit. This is
  // a physical move of a Blazor-owned DOM node, the same mechanism the portal itself
  // uses, so editor and output state ride along untouched.
  var borrowed = null;
  function returnBorrowed() {
    if (!borrowed) return;
    if (borrowed.node && borrowed.home && !borrowed.home.querySelector('[data-cell-id]')) {
      borrowed.home.appendChild(borrowed.node);
    }
    borrowed = null;
  }
  function lendActiveCell(slide) {
    var slot = slide && slide.querySelector('.vss-slide-live[data-cell-slot]');
    if (!slot || slot.querySelector('[data-cell-id]')) return;
    var cellId = slot.getAttribute('data-cell-slot');
    var home = root.querySelector('.vss-editor-slot[data-cell-slot=""' + cellId + '""]');
    var node = home && home.querySelector('[data-cell-id]');
    if (!node) return;
    slot.appendChild(node);
    borrowed = { node: node, home: home };
  }

  // Charts and other responsive outputs may have rendered while their cell was hidden
  // (in the pool or a hidden slide) and only settle their real size after the slide is
  // shown; refit the canvas when its layout size changes.
  var fitObserver = window.ResizeObserver
    ? new ResizeObserver(function () { if (presenting) fitSlide(slides()[slideIndex]); })
    : null;

  function showSlide(i) {
    var all = slides();
    if (!all.length) return;
    slideIndex = Math.max(0, Math.min(i, all.length - 1));
    returnBorrowed();
    all.forEach(function (s, n) { s.classList.toggle('is-current', n === slideIndex); });
    if (presenting) lendActiveCell(all[slideIndex]);
    var counter = root.querySelector('.vss-slide-counter');
    if (counter) counter.textContent = (slideIndex + 1) + ' / ' + all.length;
    if (fitObserver) {
      fitObserver.disconnect();
      var fit = all[slideIndex].querySelector('.vss-slide-fit');
      if (fit) fitObserver.observe(fit);
    }
    // Responsive outputs listen for window resizes; nudge them now that the slide is
    // visible, then measure after the browser lays it out.
    if (presenting) {
      try { window.dispatchEvent(new Event('resize')); } catch (e) { /* older hosts */ }
    }
    requestAnimationFrame(function () { fitSlide(all[slideIndex]); });
  }

  // Entering or leaving fullscreen and window resizes change the available canvas.
  window.addEventListener('resize', function () {
    if (!presenting) return;
    fitSlide(slides()[slideIndex]);
  });

  function enterPresenter() {
    var overlay = root.querySelector('.vss-presenter');
    if (!overlay || !slides().length) return;
    presenting = true;
    overlay.hidden = false;
    root.classList.add('vss-presenting');
    showSlide(slideIndex);
    // Best effort: hosts that disallow fullscreen (some webviews) still get the
    // viewport-filling overlay.
    if (overlay.requestFullscreen) {
      try {
        var p = overlay.requestFullscreen();
        if (p && p.catch) p.catch(function () { });
      } catch (e) { /* fullscreen unavailable */ }
    }
  }

  function exitPresenter() {
    if (!presenting) return;
    presenting = false;
    returnBorrowed();
    if (fitObserver) fitObserver.disconnect();
    var overlay = root.querySelector('.vss-presenter');
    if (overlay) overlay.hidden = true;
    root.classList.remove('vss-presenting');
    if (document.fullscreenElement && document.exitFullscreen) {
      try {
        var p = document.exitFullscreen();
        if (p && p.catch) p.catch(function () { });
      } catch (e) { /* already left */ }
    }
    if (bridge) bridge.interact('presenter-closed', String(slideIndex));
    if (renderDeferred) { renderDeferred = false; scheduleRender(); }
  }

  // Capture-phase so presenter navigation wins over the host's notebook keyboard
  // handling; inert unless presenting.
  document.addEventListener('keydown', function (e) {
    if (!presenting) return;
    if (e.key === 'ArrowRight' || e.key === 'PageDown' || e.key === ' ') {
      e.preventDefault(); e.stopPropagation(); showSlide(slideIndex + 1);
    } else if (e.key === 'ArrowLeft' || e.key === 'PageUp') {
      e.preventDefault(); e.stopPropagation(); showSlide(slideIndex - 1);
    } else if (e.key === 'Home') {
      e.preventDefault(); e.stopPropagation(); showSlide(0);
    } else if (e.key === 'End') {
      e.preventDefault(); e.stopPropagation(); showSlide(slides().length - 1);
    } else if (e.key === 'Escape') {
      e.preventDefault(); e.stopPropagation(); exitPresenter();
    }
  }, true);

  // Leaving fullscreen through browser chrome exits presenter view too; entering it
  // changes the canvas size, so refit the current slide.
  document.addEventListener('fullscreenchange', function () {
    if (!presenting) return;
    if (!document.fullscreenElement) exitPresenter();
    else requestAnimationFrame(function () { fitSlide(slides()[slideIndex]); });
  });

  // --- Notebook events: keep previews and the output pane fresh ---------------------------
  var unsubNotebook = null;
  if (bridge && bridge.onNotebookEvent) {
    unsubNotebook = bridge.onNotebookEvent(function (detail) {
      if (!document.contains(root)) { if (unsubNotebook) unsubNotebook(); return; }
      var type = detail && detail.type;
      if (type === 'cell-execution-completed' || type === 'outputs-changed' || type === 'notebook-changed') {
        scheduleRender();
      }
    });
  }

  // --- Per-render wiring ----------------------------------------------------------------
  function mount() {
    var stage = root.querySelector('.vss-root');
    if (!stage) return;

    if (!presenting) {
      slideIndex = parseInt(stage.getAttribute('data-initial-slide') || '0', 10) || 0;
    }

    // Checkboxes: filmstrip include + the two pane toggles. The change is applied
    // optimistically, persisted over cell/interact, then a re-render syncs the
    // filmstrip, panes, and prerendered deck.
    slice(stage.querySelectorAll('input[data-flag]')).forEach(function (box) {
      box.addEventListener('change', function (e) {
        e.stopPropagation();
        var cellId = box.getAttribute('data-flag-cell');
        var flag = box.getAttribute('data-flag');
        if (!cellId || !flag) return;
        if (flag === 'include') {
          var tile = stage.querySelector('.vss-tile[data-tile-cell=""' + cellId + '""]');
          if (tile) tile.classList.toggle('is-excluded', !box.checked);
        }
        setPresenterFlag(cellId, flag, box.checked).then(function () { scheduleRender(); });
      });
    });

    // Filmstrip tile click selects the cell (the include checkbox is not a select).
    stage.addEventListener('click', function (e) {
      if (e.target.closest && e.target.closest('.vss-tile-include')) return;
      var tile = e.target.closest && e.target.closest('.vss-tile[data-tile-cell]');
      if (!tile || !stage.contains(tile)) return;
      if (bridge) bridge.interact('select-cell', '', tile.getAttribute('data-tile-cell'));
    });

    // Splitter: purely local during the drag, one interaction on release to persist.
    var splitter = stage.querySelector('.vss-splitter');
    var main = stage.querySelector('.vss-main');
    if (splitter && main) {
      splitter.addEventListener('mousedown', function (e) {
        if (e.button !== 0) return;
        e.preventDefault();
        var rect = main.getBoundingClientRect();
        var ratio = null;
        root.classList.add('vss-resizing');
        function onMove(ev) {
          ratio = (ev.clientX - rect.left) / Math.max(1, rect.width);
          ratio = Math.max(0.2, Math.min(0.8, ratio));
          // One custom property drives the grid template and the live output overlay,
          // so both track the drag together.
          main.style.setProperty('--vss-split', ratio.toFixed(3));
        }
        function onUp() {
          document.removeEventListener('mousemove', onMove, true);
          document.removeEventListener('mouseup', onUp, true);
          root.classList.remove('vss-resizing');
          if (ratio !== null && bridge) bridge.interact('set-split', ratio.toFixed(3));
        }
        document.addEventListener('mousemove', onMove, true);
        document.addEventListener('mouseup', onUp, true);
      });
    }

    var presentBtn = stage.querySelector('.vss-present');
    if (presentBtn) presentBtn.addEventListener('click', function () { enterPresenter(); });

    slice(stage.querySelectorAll('.vss-hud-btn')).forEach(function (btn) {
      btn.addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        var nav = btn.getAttribute('data-nav');
        if (nav === 'prev') showSlide(slideIndex - 1);
        else if (nav === 'next') showSlide(slideIndex + 1);
        else if (nav === 'exit') exitPresenter();
      });
    });

    root.__vssStage = stage;
  }

  mount();

  // The host swaps the layout root's children on every requestRender(); re-mount when it does.
  if (window.MutationObserver && !root.__vssObserver) {
    var obs = new MutationObserver(function () {
      var s = root.querySelector('.vss-root');
      if (s && s !== root.__vssStage) mount();
    });
    obs.observe(root, { childList: true, subtree: false });
    root.__vssObserver = obs;
  }
})();
";
}
