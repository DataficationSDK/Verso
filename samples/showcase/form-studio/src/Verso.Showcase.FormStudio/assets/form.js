// Form Studio — the dashboard-builder frame. Runs inside the host-provided sandboxed iframe.
//
// Contract (same as every isolated Verso layout):
//   - window.verso is installed by the host bridge before this module runs.
//   - verso.onMessage(handler)   delivers host -> frame messages as (type, payload).
//   - verso.ready()              tells the host the frame finished initializing.
//   - verso.interact(type, pl)   sends a layout interaction to the C# extension.
//
// The host writes the active theme to :root as --verso-* custom properties (and re-writes them on
// theme change), so the chrome is themed entirely through CSS variables. Chart.js paints to a
// canvas, so it cannot inherit CSS variables; we read the tokens and feed them to the chart, then
// repaint on verso/themeChanged. No network is available inside the frame: the chart library
// arrives in-process as the string __FORM_CHARTJS__ (injected as a classic <script>, because its
// UMD wrapper binds the global through top-level `this`, which an ES module does not provide), and
// all data arrives over the message channel.
//
// The canvas is authored here, in the frame, and is instant — dragging, resizing, and configuring
// widgets never round-trip to the kernel. Only meaningful events cross the bridge: an input value
// changed (set-value), a chart needs data (bind-chart), or the document changed (save-doc, so the
// host can persist the built app through layout metadata).

const verso = window.verso;

// --- Chart library ----------------------------------------------------------

(function injectChart() {
  const script = document.createElement("script");
  script.textContent = __FORM_CHARTJS__;
  document.head.appendChild(script);
})();

const Chart = window.Chart;

// --- Icons (inline SVG, themed by currentColor) -----------------------------

const SVG = (body) => `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round" xmlns="http://www.w3.org/2000/svg">${body}</svg>`;
const ICON_GRID = SVG('<rect x="2" y="2.5" width="5" height="5" rx="1"/><rect x="9" y="2.5" width="5" height="5" rx="1"/><rect x="2" y="9" width="5" height="4.5" rx="1"/><path d="M9.5 13.5v-3M11.5 13.5v-2M13.5 13.5V9.5"/>');
const ICON_PLAY = SVG('<path d="M5 3.5l7 4.5-7 4.5z"/>');
const ICON_SLIDER = SVG('<path d="M2 8h12"/><circle cx="6" cy="8" r="2.2" fill="var(--verso-bg-default,#14161c)"/>');
const ICON_HASH = SVG('<path d="M6 2.5L4.5 13.5M11.5 2.5L10 13.5M3 5.5h11M2 10.5h11"/>');
const ICON_TOGGLE = SVG('<rect x="2" y="5" width="12" height="6" rx="3"/><circle cx="10.5" cy="8" r="1.8" fill="currentColor"/>');
const ICON_LIST = SVG('<path d="M3 4.5h10M3 8h10M3 11.5h7"/>');
const ICON_TEXT = SVG('<path d="M4 4h8M8 4v9M6.5 13h3"/>');
const ICON_TAG = SVG('<path d="M2.5 8.5l5-5h5v5l-5 5z"/><circle cx="10.5" cy="5.5" r=".8" fill="currentColor"/>');
const ICON_BAR = SVG('<path d="M2.5 13.5h11"/><rect x="3.5" y="8" width="2.2" height="4.5"/><rect x="7" y="5" width="2.2" height="7.5"/><rect x="10.5" y="9.5" width="2.2" height="3"/>');
const ICON_LINE = SVG('<path d="M2.5 13.5h11"/><path d="M3 11l3-3 3 1.5 4-5.5"/>');
const ICON_PIE = SVG('<path d="M8 2.5a5.5 5.5 0 1 0 5.5 5.5H8z"/><path d="M8 2.5V8"/>');
const ICON_SCATTER = SVG('<path d="M2.5 13.5h11"/><circle cx="5" cy="9" r="1"/><circle cx="8" cy="6" r="1"/><circle cx="11" cy="10" r="1"/><circle cx="12.5" cy="5" r="1"/>');

// --- State ------------------------------------------------------------------

// doc.widgets: [{ id, kind, x, y, w, h, label, bindVar, config }]
let doc = { widgets: [], autoRun: true };
let vars = { dataBlockVars: [] };
let mode = "edit"; // "edit" | "preview"
let selectedId = null;
let saveTimer = 0;
let widgetSeq = 0;

const chartInstances = new Map(); // widgetId -> Chart.js instance
const chartCache = new Map();     // widgetId -> { columns, types, rows }

const INPUT_KINDS = ["slider", "number", "toggle", "dropdown", "text", "label"];
const CHART_TYPES = ["bar", "line", "pie", "doughnut", "scatter"];
// A small categorical palette for multi-series charts, used when a series has no explicit color.
const SERIES_PALETTE = ["#5b8def", "#3fb27f", "#e0a04d", "#d4654f", "#9b6dd6", "#46b3c9"];

// --- Chrome -----------------------------------------------------------------

const STYLE = `
  :root { color-scheme: light dark; }
  * { box-sizing: border-box; }
  html, body { height: 100%; margin: 0; }
  body {
    background: var(--verso-bg-default, #14161c);
    color: var(--verso-fg-default, #e6e6e6);
    font-family: var(--verso-font-family-sans, system-ui, sans-serif);
    font-size: var(--verso-font-size-base, 13px);
    overflow: hidden;
  }
  .app { display: grid; grid-template-rows: auto 1fr; height: 100%; }

  .top {
    display: flex; align-items: center; gap: 10px;
    padding: 0 12px; height: 46px;
    background: var(--verso-bg-elevated, #1b1e26);
    border-bottom: 1px solid var(--verso-border-default, #2a2e38);
  }
  .brand { display: flex; align-items: center; gap: 9px; font-weight: 700; letter-spacing: .2px; }
  .brand .mark {
    width: 22px; height: 22px; border-radius: 6px;
    background: var(--verso-accent, #5b8def);
    display: grid; place-items: center; color: #fff;
  }
  .brand .mark svg { width: 14px; height: 14px; }
  .brand .sub { color: var(--verso-fg-muted, #9aa0ac); font-weight: 500; font-size: .85em; }
  .top .spacer { flex: 1; }
  .btn {
    appearance: none; border: 1px solid var(--verso-border-default, #2a2e38);
    background: var(--verso-bg-default, #14161c); color: inherit;
    border-radius: 7px; padding: 6px 11px; font: inherit; cursor: pointer;
    display: inline-flex; align-items: center; gap: 6px;
  }
  .btn:hover { border-color: var(--verso-accent, #5b8def); }
  .btn.primary { background: var(--verso-accent, #5b8def); border-color: transparent; color: #fff; }
  .seg { display: inline-flex; border: 1px solid var(--verso-border-default, #2a2e38); border-radius: 7px; overflow: hidden; }
  .seg button { appearance: none; border: 0; background: transparent; color: inherit; font: inherit; padding: 6px 12px; cursor: pointer; }
  .seg button.on { background: var(--verso-accent, #5b8def); color: #fff; }
  .toggle { display: inline-flex; align-items: center; gap: 7px; color: var(--verso-fg-muted, #9aa0ac); cursor: pointer; user-select: none; }
  .toggle input { accent-color: var(--verso-accent, #5b8def); }

  .body { display: grid; grid-template-columns: 188px 1fr 238px; min-height: 0; }
  .body.preview { grid-template-columns: 1fr; }
  .body.preview .rail { display: none; }

  .rail {
    background: var(--verso-bg-elevated, #1b1e26);
    border-right: 1px solid var(--verso-border-default, #2a2e38);
    overflow: auto; padding: 12px;
  }
  .rail.right { border-right: 0; border-left: 1px solid var(--verso-border-default, #2a2e38); }
  .rail h4 { margin: 4px 0 8px; font-size: .72em; text-transform: uppercase; letter-spacing: .6px; color: var(--verso-fg-muted, #9aa0ac); }
  .chip {
    display: flex; align-items: center; gap: 8px; padding: 8px 10px; margin-bottom: 7px;
    border: 1px solid var(--verso-border-default, #2a2e38); border-radius: 8px;
    background: var(--verso-bg-default, #14161c); cursor: grab; user-select: none;
  }
  .chip:hover { border-color: var(--verso-accent, #5b8def); }
  .chip .ic { width: 18px; height: 18px; color: var(--verso-accent, #5b8def); flex: none; display: grid; place-items: center; }
  .chip .ic svg { width: 18px; height: 18px; }

  .canvas-wrap { position: relative; overflow: auto; min-height: 0; }
  .canvas {
    position: relative; min-height: 100%; min-width: 100%;
    background-image: radial-gradient(var(--verso-border-default, #2a2e38) 1px, transparent 1px);
    background-size: 20px 20px;
  }
  .canvas.preview { background-image: none; }
  .empty {
    position: absolute; inset: 0; display: grid; place-items: center; pointer-events: none;
    color: var(--verso-fg-muted, #9aa0ac); text-align: center; padding: 24px;
  }

  .widget {
    position: absolute; border: 1px solid var(--verso-border-default, #2a2e38); border-radius: 10px;
    background: var(--verso-bg-elevated, #1b1e26); display: flex; flex-direction: column; overflow: hidden;
    box-shadow: 0 1px 2px rgba(0,0,0,.18);
  }
  .widget.sel { border-color: var(--verso-accent, #5b8def); box-shadow: 0 0 0 1px var(--verso-accent, #5b8def); }
  .widget .hd {
    display: flex; align-items: center; gap: 6px; padding: 4px 8px; cursor: move;
    border-bottom: 1px solid var(--verso-border-default, #2a2e38);
    color: var(--verso-fg-muted, #9aa0ac); font-size: .82em;
  }
  .widget .hd .t { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .widget .bd { flex: 1; padding: 10px; display: flex; flex-direction: column; gap: 6px; min-height: 0; }
  .widget.preview .hd { display: none; }
  .widget .lbl { font-size: .82em; color: var(--verso-fg-muted, #9aa0ac); }
  .widget .val { font-variant-numeric: tabular-nums; color: var(--verso-fg-default, #e6e6e6); }
  .widget .rz {
    position: absolute; right: 2px; bottom: 2px; width: 14px; height: 14px; cursor: nwse-resize;
    border-right: 2px solid var(--verso-fg-muted, #9aa0ac); border-bottom: 2px solid var(--verso-fg-muted, #9aa0ac);
    border-bottom-right-radius: 4px; opacity: .5;
  }
  .widget.preview .rz { display: none; }
  .chart-host { position: relative; flex: 1; min-height: 0; }

  input[type=range] { width: 100%; accent-color: var(--verso-accent, #5b8def); }
  input[type=text], input[type=number], select {
    width: 100%; box-sizing: border-box; padding: 5px 7px; font: inherit;
    color: inherit; background: var(--verso-bg-default, #14161c);
    border: 1px solid var(--verso-border-default, #2a2e38); border-radius: 6px;
  }
  .switch { display: inline-flex; align-items: center; gap: 8px; cursor: pointer; }
  .switch input { width: 16px; height: 16px; accent-color: var(--verso-accent, #5b8def); }

  .field { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
  .field > label { font-size: .76em; color: var(--verso-fg-muted, #9aa0ac); }
  .field .row { display: flex; gap: 6px; }
  .hint { color: var(--verso-fg-muted, #9aa0ac); font-size: .82em; line-height: 1.5; }
  .del { color: var(--verso-danger, #d4654f); border-color: var(--verso-danger, #d4654f); }
`;

injectStyle(STYLE);

document.body.innerHTML = `
  <div class="app">
    <div class="top">
      <div class="brand">
        <span class="mark">${ICON_GRID}</span>
        <span>Form Studio</span><span class="sub">dashboard builder</span>
      </div>
      <div class="spacer"></div>
      <label class="toggle" title="Re-run the notebook automatically when an input changes">
        <input type="checkbox" id="autorun" checked> Auto-run
      </label>
      <button class="btn" id="run" title="Re-run the notebook now">${ICON_PLAY} Run</button>
      <button class="btn" id="export" title="Download the dashboard as JSON">Export</button>
      <div class="seg" id="mode">
        <button data-mode="edit" class="on">Edit</button>
        <button data-mode="preview">Preview</button>
      </div>
    </div>
    <div class="body" id="body">
      <div class="rail left" id="palette"></div>
      <div class="canvas-wrap" id="canvasWrap"><div class="canvas" id="canvas"></div></div>
      <div class="rail right" id="props"></div>
    </div>
  </div>
`;

const canvasEl = document.getElementById("canvas");
const canvasWrapEl = document.getElementById("canvasWrap");
const bodyEl = document.getElementById("body");
const propsEl = document.getElementById("props");
const autorunEl = document.getElementById("autorun");

// --- Palette (drag source + click-to-add) -----------------------------------

const PALETTE = [
  { group: "Inputs", items: [
    { kind: "slider", label: "Slider", icon: ICON_SLIDER },
    { kind: "number", label: "Number", icon: ICON_HASH },
    { kind: "toggle", label: "Toggle", icon: ICON_TOGGLE },
    { kind: "dropdown", label: "Dropdown", icon: ICON_LIST },
    { kind: "text", label: "Text", icon: ICON_TEXT },
    { kind: "label", label: "Label", icon: ICON_TAG },
  ]},
  { group: "Charts", items: [
    { kind: "chart", chartType: "bar", label: "Bar", icon: ICON_BAR },
    { kind: "chart", chartType: "line", label: "Line", icon: ICON_LINE },
    { kind: "chart", chartType: "pie", label: "Pie", icon: ICON_PIE },
    { kind: "chart", chartType: "scatter", label: "Scatter", icon: ICON_SCATTER },
  ]},
];

function buildPalette() {
  const host = document.getElementById("palette");
  host.innerHTML = "";
  for (const section of PALETTE) {
    const h = el("h4", {}, section.group);
    host.appendChild(h);
    for (const item of section.items) {
      const chip = el("div", { class: "chip", draggable: "true", title: "Drag onto the canvas" },
        el("span", { class: "ic", html: item.icon }), el("span", {}, item.label));
      chip.addEventListener("dragstart", (e) => {
        e.dataTransfer.setData("text/plain", JSON.stringify({ kind: item.kind, chartType: item.chartType }));
        e.dataTransfer.effectAllowed = "copy";
      });
      // Click-to-add as a fallback (drops at a cascading position) for environments that limit DnD.
      chip.addEventListener("click", () => addWidget(item.kind, item.chartType, 24 + (doc.widgets.length % 6) * 18, 24 + (doc.widgets.length % 6) * 18));
      host.appendChild(chip);
    }
  }
}

canvasEl.addEventListener("dragover", (e) => { if (mode === "edit") { e.preventDefault(); e.dataTransfer.dropEffect = "copy"; } });
canvasEl.addEventListener("drop", (e) => {
  if (mode !== "edit") return;
  e.preventDefault();
  let spec;
  try { spec = JSON.parse(e.dataTransfer.getData("text/plain")); } catch { return; }
  if (!spec || !spec.kind) return;
  const r = canvasEl.getBoundingClientRect();
  addWidget(spec.kind, spec.chartType, Math.max(0, e.clientX - r.left - 40), Math.max(0, e.clientY - r.top - 16));
});

// Initial paint (after the palette and its data are defined, to avoid touching them too early).
buildPalette();
renderProps();
updateEmpty();

// --- Widget model -----------------------------------------------------------

function addWidget(kind, chartType, x, y) {
  const id = "w" + (++widgetSeq) + "_" + doc.widgets.length;
  const w = { id, kind, x: Math.round(x), y: Math.round(y), label: defaultLabel(kind, chartType), bindVar: "", config: {} };
  applyDefaults(w, chartType);
  doc.widgets.push(w);
  renderWidget(w);
  select(id);
  saveDoc();
  if (w.kind === "chart" && w.config.sourceVar) requestChart(w);
}

function defaultLabel(kind, chartType) {
  if (kind === "chart") return cap(chartType || "bar") + " chart";
  if (kind === "label") return "Heading";
  return cap(kind);
}

function applyDefaults(w, chartType) {
  switch (w.kind) {
    case "slider":  w.w = 230; w.h = 96;  w.config = { min: 0, max: 100, step: 1 }; w.value = 50; w.bindVar = nextVar("value"); break;
    case "number":  w.w = 190; w.h = 90;  w.value = 0; w.bindVar = nextVar("value"); break;
    case "toggle":  w.w = 190; w.h = 84;  w.value = false; w.bindVar = nextVar("flag"); break;
    case "dropdown":w.w = 210; w.h = 92;  w.config = { options: ["All", "A", "B", "C"] }; w.value = "All"; w.bindVar = nextVar("choice"); break;
    case "text":    w.w = 230; w.h = 90;  w.value = ""; w.bindVar = nextVar("text"); break;
    case "label":   w.w = 230; w.h = 56;  w.config = { text: "Heading" }; break;
    case "chart":   w.w = 380; w.h = 270; w.config = { sourceVar: vars.dataBlockVars[0] || "", chartType: chartType || "bar", xColumn: "", yColumns: [], color: "" }; break;
  }
}

function nextVar(base) {
  let n = 1, name;
  const used = new Set(doc.widgets.map((w) => w.bindVar).filter(Boolean));
  do { name = base + n++; } while (used.has(name));
  return name;
}

function widgetById(id) { return doc.widgets.find((w) => w.id === id); }

function removeWidget(id) {
  const i = doc.widgets.findIndex((w) => w.id === id);
  if (i < 0) return;
  doc.widgets.splice(i, 1);
  destroyChart(id);
  const node = document.getElementById("node_" + id);
  if (node) node.remove();
  if (selectedId === id) select(null);
  saveDoc();
}

// --- Widget rendering -------------------------------------------------------

function renderAll() {
  canvasEl.querySelectorAll(".widget").forEach((n) => n.remove());
  for (const w of doc.widgets) renderWidget(w);
  updateEmpty();
}

function renderWidget(w) {
  let node = document.getElementById("node_" + w.id);
  if (!node) {
    node = el("div", { class: "widget", id: "node_" + w.id });
    node.addEventListener("pointerdown", (e) => { if (mode === "edit" && !e.target.closest(".rz")) select(w.id); });
    canvasEl.appendChild(node);
  }
  node.className = "widget" + (w.id === selectedId ? " sel" : "") + (mode === "preview" ? " preview" : "");
  node.style.left = w.x + "px";
  node.style.top = w.y + "px";
  node.style.width = w.w + "px";
  node.style.height = w.h + "px";

  node.innerHTML = "";
  const hd = el("div", { class: "hd" }, el("span", { class: "t" }, w.label || cap(w.kind)));
  hd.addEventListener("pointerdown", (e) => startMove(e, w));
  node.appendChild(hd);

  const bd = el("div", { class: "bd" });
  node.appendChild(bd);
  renderBody(w, bd);

  const rz = el("div", { class: "rz", title: "Resize" });
  rz.addEventListener("pointerdown", (e) => startResize(e, w));
  node.appendChild(rz);

  updateEmpty();
}

function renderBody(w, bd) {
  switch (w.kind) {
    case "label":
      bd.appendChild(el("div", { style: "font-weight:700;font-size:1.05em" }, w.config.text || w.label));
      break;
    case "slider": {
      const head = el("div", { style: "display:flex;justify-content:space-between" },
        el("span", { class: "lbl" }, w.label), el("span", { class: "val" }, String(w.value)));
      const r = el("input", { type: "range", min: w.config.min, max: w.config.max, step: w.config.step, value: w.value });
      r.addEventListener("input", () => { w.value = num(r.value); head.lastChild.textContent = String(w.value); emitValue(w); });
      bd.append(head, r);
      break;
    }
    case "number": {
      bd.appendChild(el("div", { class: "lbl" }, w.label));
      const inp = el("input", { type: "number", value: w.value });
      inp.addEventListener("input", () => { w.value = num(inp.value); emitValue(w); });
      bd.appendChild(inp);
      break;
    }
    case "toggle": {
      const lab = el("label", { class: "switch" });
      const cb = el("input", { type: "checkbox" });
      cb.checked = !!w.value;
      cb.addEventListener("change", () => { w.value = cb.checked; emitValue(w); });
      lab.append(cb, el("span", {}, w.label));
      bd.appendChild(lab);
      break;
    }
    case "dropdown": {
      bd.appendChild(el("div", { class: "lbl" }, w.label));
      const sel = el("select", {});
      for (const o of (w.config.options || [])) sel.appendChild(el("option", { value: o, selected: o === w.value ? "" : null }, o));
      sel.value = w.value;
      sel.addEventListener("change", () => { w.value = sel.value; emitValue(w); });
      bd.appendChild(sel);
      break;
    }
    case "text": {
      bd.appendChild(el("div", { class: "lbl" }, w.label));
      const inp = el("input", { type: "text", value: w.value || "" });
      inp.addEventListener("input", () => { w.value = inp.value; emitValue(w); });
      bd.appendChild(inp);
      break;
    }
    case "chart": {
      const host = el("div", { class: "chart-host" });
      const cv = el("canvas", { id: "cv_" + w.id });
      host.appendChild(cv);
      bd.appendChild(host);
      // Defer until the node has a measured size, then paint from whatever data we have cached.
      requestAnimationFrame(() => renderChart(w));
      break;
    }
  }
}

function updateEmpty() {
  let e = canvasEl.querySelector(".empty");
  if (doc.widgets.length === 0) {
    if (!e) canvasEl.appendChild(el("div", { class: "empty" },
      el("div", {}, "Drag a widget from the left onto the canvas.", el("br"), el("br"),
        el("span", { class: "hint" }, "Inputs write kernel variables. Charts plot a DataBlock. Switch to Preview to use the app."))));
  } else if (e) {
    e.remove();
  }
}

// --- Move + resize ----------------------------------------------------------

function startMove(e, w) {
  if (mode !== "edit") return;
  e.preventDefault();
  select(w.id);
  const node = document.getElementById("node_" + w.id);
  const sx = e.clientX, sy = e.clientY, ox = w.x, oy = w.y;
  node.setPointerCapture(e.pointerId);
  const move = (ev) => { w.x = Math.max(0, ox + (ev.clientX - sx)); w.y = Math.max(0, oy + (ev.clientY - sy)); node.style.left = w.x + "px"; node.style.top = w.y + "px"; };
  const up = (ev) => { node.releasePointerCapture(ev.pointerId); node.removeEventListener("pointermove", move); node.removeEventListener("pointerup", up); saveDoc(); };
  node.addEventListener("pointermove", move);
  node.addEventListener("pointerup", up);
}

function startResize(e, w) {
  if (mode !== "edit") return;
  e.preventDefault(); e.stopPropagation();
  const node = document.getElementById("node_" + w.id);
  const sx = e.clientX, sy = e.clientY, ow = w.w, oh = w.h;
  node.setPointerCapture(e.pointerId);
  const move = (ev) => {
    w.w = Math.max(140, ow + (ev.clientX - sx));
    w.h = Math.max(56, oh + (ev.clientY - sy));
    node.style.width = w.w + "px"; node.style.height = w.h + "px";
    if (w.kind === "chart") { const c = chartInstances.get(w.id); if (c) c.resize(); }
  };
  const up = (ev) => { node.releasePointerCapture(ev.pointerId); node.removeEventListener("pointermove", move); node.removeEventListener("pointerup", up); saveDoc(); };
  node.addEventListener("pointermove", move);
  node.addEventListener("pointerup", up);
}

// --- Selection + properties panel -------------------------------------------

function select(id) {
  selectedId = id;
  canvasEl.querySelectorAll(".widget").forEach((n) => n.classList.toggle("sel", n.id === "node_" + id));
  renderProps();
}

function renderProps() {
  propsEl.innerHTML = "";
  propsEl.appendChild(el("h4", {}, "Properties"));
  const w = widgetById(selectedId);
  if (!w) {
    propsEl.appendChild(el("div", { class: "hint" }, "Select a widget to edit its settings, or drag a new one from the palette."));
    return;
  }

  propsEl.appendChild(field("Label", textInput(w.label, (v) => { w.label = v; renderWidget(w); saveDoc(); })));

  if (w.kind !== "label" && w.kind !== "chart") {
    propsEl.appendChild(field("Binds to variable", textInput(w.bindVar, (v) => { w.bindVar = v.trim(); saveDoc(); emitValue(w); }), "The kernel variable this input writes."));
  }

  switch (w.kind) {
    case "slider":
      propsEl.appendChild(field("Min", numInput(w.config.min, (v) => cfg(w, "min", v))));
      propsEl.appendChild(field("Max", numInput(w.config.max, (v) => cfg(w, "max", v))));
      propsEl.appendChild(field("Step", numInput(w.config.step, (v) => cfg(w, "step", v))));
      break;
    case "dropdown":
      propsEl.appendChild(field("Options (comma separated)",
        textInput((w.config.options || []).join(", "), (v) => {
          w.config.options = v.split(",").map((s) => s.trim()).filter(Boolean);
          if (!w.config.options.includes(w.value)) w.value = w.config.options[0] || "";
          renderWidget(w); saveDoc(); emitValue(w);
        })));
      break;
    case "label":
      propsEl.appendChild(field("Text", textInput(w.config.text || "", (v) => cfg(w, "text", v))));
      break;
    case "chart":
      propsEl.appendChild(field("DataBlock", selectInput(vars.dataBlockVars, w.config.sourceVar, (v) => {
        w.config.sourceVar = v; w.config.xColumn = ""; w.config.yColumns = []; saveDoc(); requestChart(w);
      }), "The kernel DataBlock to plot."));
      propsEl.appendChild(field("Chart type", selectInput(CHART_TYPES, w.config.chartType, (v) => { w.config.chartType = v; renderChart(w); saveDoc(); })));
      const cols = (chartCache.get(w.id) || {}).columns || [];
      propsEl.appendChild(field("X axis", selectInput(cols, w.config.xColumn, (v) => { w.config.xColumn = v; renderChart(w); saveDoc(); })));
      propsEl.appendChild(field("Y axis (series)", multiSelect(cols, w.config.yColumns || [], (v) => { w.config.yColumns = v; renderChart(w); saveDoc(); })));
      propsEl.appendChild(field("Series color", colorInput(w.config.color || SERIES_PALETTE[0], (v) => { w.config.color = v; renderChart(w); saveDoc(); })));
      break;
  }

  const del = el("button", { class: "btn del" }, "Delete widget");
  del.addEventListener("click", () => removeWidget(w.id));
  propsEl.appendChild(del);
}

function cfg(w, key, value) { w.config[key] = value; renderWidget(w); saveDoc(); }

// --- Charts (Chart.js) ------------------------------------------------------

function requestChart(w) {
  if (w.kind !== "chart" || !w.config.sourceVar) { renderChart(w); return; }
  verso.interact("bind-chart", { id: w.id, sourceVar: w.config.sourceVar });
}

function destroyChart(id) {
  const c = chartInstances.get(id);
  if (c) { c.destroy(); chartInstances.delete(id); }
}

function renderChart(w) {
  const cv = document.getElementById("cv_" + w.id);
  if (!cv) return;
  destroyChart(w.id);

  const data = chartCache.get(w.id);
  const cols = data ? data.columns : [];
  if (!data || cols.length === 0) {
    paintPlaceholder(cv, w.config.sourceVar ? "Loading…" : "Pick a DataBlock");
    return;
  }

  // Auto-pick sensible axes the first time so a freshly dropped chart shows something.
  if (!w.config.xColumn || !cols.includes(w.config.xColumn)) w.config.xColumn = firstOf(data, (t) => t !== "numeric") || cols[0];
  if (!w.config.yColumns || w.config.yColumns.length === 0) {
    const y = firstOf(data, (t) => t === "numeric");
    w.config.yColumns = y ? [y] : cols.filter((c) => c !== w.config.xColumn).slice(0, 1);
  }

  const xi = cols.indexOf(w.config.xColumn);
  const labels = data.rows.map((r) => r[xi]);
  const type = w.config.chartType || "bar";
  const baseColor = w.config.color || SERIES_PALETTE[0];

  let chartConfig;
  if (type === "pie" || type === "doughnut") {
    const yi = cols.indexOf(w.config.yColumns[0]);
    chartConfig = {
      type,
      data: { labels, datasets: [{ data: data.rows.map((r) => num(r[yi])), backgroundColor: labels.map((_, i) => SERIES_PALETTE[i % SERIES_PALETTE.length]) }] },
      options: chartOptions(type, false),
    };
  } else if (type === "scatter") {
    const yi = cols.indexOf(w.config.yColumns[0]);
    chartConfig = {
      type: "scatter",
      data: { datasets: [{ label: w.config.yColumns[0], data: data.rows.map((r) => ({ x: num(r[xi]), y: num(r[yi]) })), backgroundColor: baseColor }] },
      options: chartOptions(type, true),
    };
  } else {
    const datasets = (w.config.yColumns || []).map((name, i) => {
      const yi = cols.indexOf(name);
      const color = i === 0 ? baseColor : SERIES_PALETTE[i % SERIES_PALETTE.length];
      return { label: name, data: data.rows.map((r) => num(r[yi])), backgroundColor: color, borderColor: color, fill: type === "line" ? false : undefined, tension: 0.25 };
    });
    chartConfig = { type, data: { labels, datasets }, options: chartOptions(type, true) };
  }

  chartInstances.set(w.id, new Chart(cv.getContext("2d"), chartConfig));
}

function chartOptions(type, hasAxes) {
  const fg = token("--verso-fg-muted", "#9aa0ac");
  const grid = token("--verso-border-default", "#2a2e38");
  const opts = {
    responsive: true, maintainAspectRatio: false, animation: { duration: 250 },
    plugins: { legend: { labels: { color: fg, boxWidth: 12, font: { size: 11 } } } },
  };
  if (hasAxes) {
    opts.scales = {
      x: { ticks: { color: fg, font: { size: 10 } }, grid: { color: grid } },
      y: { ticks: { color: fg, font: { size: 10 } }, grid: { color: grid } },
    };
  }
  return opts;
}

function paintPlaceholder(cv, text) {
  const ctx = cv.getContext("2d");
  const w = cv.clientWidth || cv.width, h = cv.clientHeight || cv.height;
  cv.width = w; cv.height = h;
  ctx.clearRect(0, 0, w, h);
  ctx.fillStyle = token("--verso-fg-muted", "#9aa0ac");
  ctx.font = "12px " + token("--verso-font-family-sans", "system-ui, sans-serif");
  ctx.textAlign = "center"; ctx.textBaseline = "middle";
  ctx.fillText(text, w / 2, h / 2);
}

function firstOf(data, pred) {
  for (let i = 0; i < data.columns.length; i++) if (pred(data.types[i])) return data.columns[i];
  return null;
}

function reThemeCharts() {
  for (const w of doc.widgets) if (w.kind === "chart") renderChart(w);
}

// --- Bridge: frame -> host --------------------------------------------------

function emitValue(w) {
  if (!w.bindVar) return;
  verso.interact("set-value", { var: w.bindVar, kind: w.kind, value: w.value });
}

function saveDoc() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => verso.interact("save-doc", { doc: JSON.stringify(serialize()) }), 200);
}

function serialize() {
  return {
    autoRun: doc.autoRun,
    widgets: doc.widgets.map((w) => ({
      id: w.id, kind: w.kind, x: w.x, y: w.y, w: w.w, h: w.h,
      label: w.label, bindVar: w.bindVar, value: w.value, config: w.config,
    })),
  };
}

// --- Toolbar wiring ---------------------------------------------------------

document.getElementById("run").addEventListener("click", () => verso.interact("run", {}));
document.getElementById("export").addEventListener("click", () =>
  verso.interact("export", { fileName: "dashboard.json", content: JSON.stringify(serialize(), null, 2) }));

autorunEl.addEventListener("change", () => { doc.autoRun = autorunEl.checked; saveDoc(); });

document.getElementById("mode").addEventListener("click", (e) => {
  const b = e.target.closest("button[data-mode]");
  if (!b) return;
  mode = b.dataset.mode;
  document.querySelectorAll("#mode button").forEach((x) => x.classList.toggle("on", x === b));
  bodyEl.classList.toggle("preview", mode === "preview");
  canvasEl.classList.toggle("preview", mode === "preview");
  if (mode === "preview") select(null);
  renderAll();
});

// --- Bridge: host -> frame --------------------------------------------------

function applySeed(seed) {
  if (seed.vars) vars = seed.vars;
  if (seed.doc) {
    const parsed = typeof seed.doc === "string" ? safeParse(seed.doc) : seed.doc;
    if (parsed && Array.isArray(parsed.widgets)) {
      doc = { autoRun: parsed.autoRun !== false, widgets: parsed.widgets };
      widgetSeq = doc.widgets.length;
    }
  }
  autorunEl.checked = doc.autoRun;
  renderAll();
  renderProps();
  // Ask the host for data for any chart restored from the saved document.
  for (const w of doc.widgets) if (w.kind === "chart" && w.config.sourceVar) requestChart(w);
  // Seed the kernel variables from the restored input widgets, so a prepopulated dashboard is
  // consistent on open. With auto-run on, this triggers one recompute to match the widget values.
  for (const w of doc.widgets) if (w.bindVar) emitValue(w);
}

verso.onMessage((type, payload) => {
  switch (type) {
    case "verso/init":
      if (payload && payload.extension) applySeed(payload.extension);
      break;
    case "ext/vars":
      if (payload) { vars = payload; if (widgetById(selectedId)) renderProps(); }
      break;
    case "ext/chart-data":
      if (payload && payload.id) {
        chartCache.set(payload.id, payload.data || { columns: [], types: [], rows: [] });
        const w = widgetById(payload.id);
        if (w) { renderChart(w); if (selectedId === w.id) renderProps(); }
      }
      break;
    case "verso/themeChanged":
      // Chrome restyles automatically via CSS variables; canvas charts must repaint.
      reThemeCharts();
      break;
  }
});

verso.ready();
verso.interact("request-vars", {});

// --- Small helpers ----------------------------------------------------------

function el(tag, props, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) {
    if (v === null || v === undefined) continue;
    if (k === "class") node.className = v;
    else if (k === "html") node.innerHTML = v;
    else if (k === "style") node.setAttribute("style", v);
    else node.setAttribute(k, v);
  }
  for (const c of children.flat()) {
    if (c === null || c === undefined) continue;
    node.appendChild(typeof c === "string" ? document.createTextNode(c) : c);
  }
  return node;
}

function field(label, control, hint) {
  return el("div", { class: "field" }, el("label", {}, label), control, hint ? el("div", { class: "hint" }, hint) : null);
}
function textInput(value, onChange) {
  const i = el("input", { type: "text", value: value || "" });
  i.addEventListener("change", () => onChange(i.value));
  return i;
}
function numInput(value, onChange) {
  const i = el("input", { type: "number", value: value });
  i.addEventListener("change", () => onChange(num(i.value)));
  return i;
}
function colorInput(value, onChange) {
  const i = el("input", { type: "color", value: toHex(value), style: "width:46px;height:30px;padding:2px;border-radius:6px;cursor:pointer" });
  i.addEventListener("input", () => onChange(i.value));
  return i;
}
function selectInput(options, value, onChange) {
  const s = el("select", {});
  if (!options || options.length === 0) s.appendChild(el("option", { value: "" }, "(none)"));
  for (const o of (options || [])) s.appendChild(el("option", { value: o }, o));
  s.value = value || "";
  s.addEventListener("change", () => onChange(s.value));
  return s;
}
function multiSelect(options, selected, onChange) {
  const wrap = el("div", { class: "row", style: "flex-wrap:wrap" });
  for (const o of (options || [])) {
    const id = "ms_" + Math.abs(hash(o));
    const lab = el("label", { class: "toggle", style: "font-size:.82em" });
    const cb = el("input", { type: "checkbox" });
    cb.checked = selected.includes(o);
    cb.addEventListener("change", () => {
      const set = new Set(selected);
      cb.checked ? set.add(o) : set.delete(o);
      onChange((options || []).filter((c) => set.has(c)));
    });
    lab.append(cb, document.createTextNode(o));
    wrap.appendChild(lab);
  }
  if (!options || options.length === 0) wrap.appendChild(el("span", { class: "hint" }, "Pick a DataBlock first."));
  return wrap;
}

function num(v) { const n = Number(v); return Number.isFinite(n) ? n : 0; }
function cap(s) { return s ? s[0].toUpperCase() + s.slice(1) : s; }
function token(name, fallback) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback; }
function safeParse(s) { try { return JSON.parse(s); } catch { return null; } }
function hash(s) { let h = 0; for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0; return h; }
function toHex(c) {
  if (!c) return "#5b8def";
  if (c[0] === "#") return c.length === 4 ? "#" + [...c.slice(1)].map((x) => x + x).join("") : c.slice(0, 7);
  return "#5b8def";
}
function injectStyle(css) { const s = document.createElement("style"); s.textContent = css; document.head.appendChild(s); }
