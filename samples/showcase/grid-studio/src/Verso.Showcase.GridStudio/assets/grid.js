// Grid Studio — the spreadsheet frame. Runs inside the host-provided sandboxed iframe.
//
// Contract (same as every isolated Verso layout):
//   - window.verso is installed by the host bridge before this module runs.
//   - verso.onMessage(handler)   delivers host -> frame messages as (type, payload).
//   - verso.ready()              tells the host the frame finished initializing.
//   - verso.interact(type, pl)   sends a layout interaction to the C# extension.
//
// The host writes the active theme to :root as --verso-* custom properties (and re-writes them
// on theme change), so the chrome and the grid are themed entirely through CSS variables and
// track every Verso theme. No network is available inside the frame; the grid library, its
// styles, and the data all arrive in-process — the libraries as classic <script> injections
// (see below), the data over the message channel.
//
// The grid itself is Jspreadsheet CE (MIT) with its helper jsuites (MIT), both vendored. They
// are UMD bundles that bind their global through `this` and alias an existing global with a
// top-level `var`; those semantics only hold in a classic script at global scope, so the C#
// assembler hands them to us as the strings __GRID_JSUITES__ / __GRID_JSPREADSHEET__ and we
// inject them as inline classic scripts here rather than importing them as modules.

const verso = window.verso;

// --- Vendor assets: styles + libraries --------------------------------------

(function injectVendor() {
  injectStyle(__GRID_VENDOR_CSS__);

  // Classic <script> injection: top-level `this` is window and top-level `var` aliases globals,
  // which is what these UMD bundles assume. jsuites first; jspreadsheet reads it as jSuites.
  injectScript(__GRID_JSUITES__);
  injectScript(__GRID_JSPREADSHEET__);
})();

function injectScript(source) {
  const script = document.createElement("script");
  script.textContent = source;
  document.head.appendChild(script);
}

function injectStyle(css) {
  const style = document.createElement("style");
  style.textContent = css;
  document.head.appendChild(style);
}

const jspreadsheet = window.jspreadsheet;

// --- State ------------------------------------------------------------------

let sourceVar = "data";
// model.columns: [{ name, type }] where type is numeric | text | checkbox | calendar.
// model.rows:    array of row arrays. Mirrors the C# GridData shape.
let model = null;
let instance = null;     // the live Jspreadsheet worksheet
let dirty = false;
let commitTimer = 0;
let selRange = null;     // [firstRow, lastRow] of the current selection, or null
let readOnly = false;    // true when the bound source is a DataTable (view only, never committed)

// --- Chrome (themed entirely via --verso-* tokens) --------------------------

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
  .app { display: grid; grid-template-rows: auto 1fr auto; height: 100%; }

  /* Top bar */
  .top {
    display: flex; align-items: center; gap: 10px;
    padding: 0 12px; height: 46px;
    background: var(--verso-bg-elevated, #1b1e26);
    border-bottom: 1px solid var(--verso-border-default, #2a2e38);
  }
  .top .spacer { flex: 1; }

  .bind { display: flex; align-items: center; gap: 6px; color: var(--verso-fg-muted, #9aa0ac); }
  .bind label { font-size: .85em; }
  .bind select {
    background: color-mix(in srgb, var(--verso-fg-default, #fff) 6%, transparent);
    border: 1px solid var(--verso-border-default, #2a2e38);
    color: var(--verso-fg-default, #e6e6e6);
    border-radius: 7px; padding: 5px 8px; min-width: 120px; font: inherit;
  }
  .bind select:focus { outline: none; border-color: var(--verso-accent, #5b8def); }
  /* Native option lists render with the system palette; keep them legible on dark themes. */
  .bind select option { background: var(--verso-bg-elevated, #1b1e26); color: var(--verso-fg-default, #e6e6e6); }

  .btn {
    appearance: none; cursor: pointer; user-select: none;
    border: 1px solid var(--verso-border-default, #2a2e38);
    background: color-mix(in srgb, var(--verso-fg-default, #fff) 6%, transparent);
    color: var(--verso-fg-default, #e6e6e6);
    border-radius: 8px; padding: 6px 10px; font: inherit;
    display: inline-flex; align-items: center; gap: 6px;
    transition: background .12s ease, border-color .12s ease, transform .06s ease;
  }
  .btn:hover { background: color-mix(in srgb, var(--verso-fg-default, #fff) 12%, transparent); }
  .btn:active { transform: translateY(1px); }
  .btn:disabled { opacity: .45; cursor: default; pointer-events: none; }
  .btn.accent { background: var(--verso-accent, #5b8def); border-color: transparent; color: #fff; }
  .btn.accent:hover { filter: brightness(1.08); }
  .btn svg { width: 14px; height: 14px; }

  /* Grid area */
  .stage { position: relative; min-height: 0; overflow: auto; padding: 10px; }
  .grid-host { display: inline-block; min-width: 100%; }

  /* Empty state */
  .empty {
    position: absolute; inset: 0; display: none;
    flex-direction: column; align-items: center; justify-content: center; gap: 10px;
    text-align: center; padding: 24px; color: var(--verso-fg-muted, #9aa0ac);
  }
  .empty.show { display: flex; }
  .empty h2 { margin: 0; font-size: 1.1em; color: var(--verso-fg-default, #e6e6e6); }
  .empty code {
    font-family: var(--verso-font-family-mono, ui-monospace, monospace);
    background: color-mix(in srgb, var(--verso-fg-default, #fff) 8%, transparent);
    padding: 1px 6px; border-radius: 5px;
  }

  /* Status bar */
  .status {
    display: flex; align-items: center; gap: 14px;
    padding: 0 12px; height: 28px;
    background: var(--verso-bg-elevated, #1b1e26);
    border-top: 1px solid var(--verso-border-default, #2a2e38);
    color: var(--verso-fg-muted, #9aa0ac); font-size: .85em;
  }
  .status .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--verso-fg-muted, #9aa0ac); }
  .status.dirty .dot { background: var(--verso-accent, #5b8def); }
  .status .spacer { flex: 1; }

  /* --- Jspreadsheet theme overlay: re-skin the grid with --verso-* tokens --- */
  /* Paint the table surfaces with the theme background so jspreadsheet's default white
     backdrop never shows through a semi-transparent selection highlight. */
  .jexcel_container, .jexcel, .jexcel_content {
    font-family: inherit;
    color: var(--verso-fg-default, #e6e6e6);
    background-color: var(--verso-bg-default, #14161c);
  }
  .jexcel > thead > tr > td {
    background-color: var(--verso-bg-elevated, #1b1e26);
    color: var(--verso-fg-muted, #9aa0ac);
    border-color: var(--verso-border-default, #2a2e38);
    font-weight: 600;
  }
  .jexcel > tbody > tr > td {
    background-color: var(--verso-bg-default, #14161c);
    color: var(--verso-fg-default, #e6e6e6);
    border-color: var(--verso-border-default, #2a2e38);
  }
  .jexcel > tbody > tr > td.jexcel_row {
    background-color: var(--verso-bg-elevated, #1b1e26);
    color: var(--verso-fg-muted, #9aa0ac);
    border-color: var(--verso-border-default, #2a2e38);
  }
  /* Selected column/row headers (jspreadsheet defaults to a light #dcdcdc that is unreadable on
     a dark theme). A solid accent with white text reads on any theme and needs no color-mix. */
  .jexcel > thead > tr > td.selected,
  .jexcel > tbody > tr.selected > td:first-child {
    background-color: var(--verso-accent, #5b8def) !important;
    color: #ffffff !important;
  }
  /* Cells inside the selection range. rgba fallback first, color-mix as enhancement, so the
     background never falls back to a light default and leaves light text unreadable. */
  .jexcel > tbody > tr > td.highlight {
    background-color: #25344d !important;
    background-color: color-mix(in srgb, var(--verso-accent, #5b8def) 28%, var(--verso-bg-default, #14161c)) !important;
    color: var(--verso-fg-default, #e6e6e6) !important;
  }
  /* The active cell within the selection: a touch stronger, still readable. */
  .jexcel > tbody > tr > td.highlight-selected {
    background-color: #33496b !important;
    background-color: color-mix(in srgb, var(--verso-accent, #5b8def) 42%, var(--verso-bg-default, #14161c)) !important;
    color: var(--verso-fg-default, #e6e6e6) !important;
  }
  .jexcel_container .jexcel_corner { background-color: var(--verso-accent, #5b8def); }
  /* The editor input that appears on double-click */
  .jexcel_container td .jexcel_input,
  .jexcel_container input.editor {
    background: var(--verso-bg-default, #14161c);
    color: var(--verso-fg-default, #e6e6e6);
  }
`;

// Our overlay goes in after the vendored CSS so its theme rules win the cascade.
injectStyle(STYLE);

// --- DOM scaffold -----------------------------------------------------------

const ICON = {
  addRow: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M3 6h18M3 12h18M3 18h10"/><path d="M18 15v6M15 18h6"/></svg>`,
  addCol: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M6 3v18M12 3v18M18 3v10"/><path d="M18 16v6M15 19h6"/></svg>`,
  delRow: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M3 6h18M3 12h12M3 18h12"/><path d="M16 15l5 5M21 15l-5 5"/></svg>`,
  commit: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>`,
  reload: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12a9 9 0 1 1-3-6.7L21 8"/><path d="M21 3v5h-5"/></svg>`,
  export: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v12M8 11l4 4 4-4"/><path d="M4 21h16"/></svg>`,
};

document.documentElement.style.height = "100%";
document.body.innerHTML = `
  <div class="app">
    <div class="top">
      <button class="btn" id="addRow">${ICON.addRow} Row</button>
      <button class="btn" id="addCol">${ICON.addCol} Column</button>
      <button class="btn" id="delRow">${ICON.delRow} Delete</button>
      <button class="btn" id="export">${ICON.export} CSV</button>
      <button class="btn" id="reload">${ICON.reload} Reload</button>
      <button class="btn accent" id="commit">${ICON.commit} Commit</button>
      <div class="spacer"></div>
      <div class="bind"><label>Data</label><select id="sourceVar"></select></div>
    </div>
    <div class="stage">
      <div class="grid-host" id="gridHost"></div>
      <div class="empty" id="empty">
        <h2>No data bound</h2>
        <div id="emptyMsg"></div>
      </div>
    </div>
    <div class="status" id="status">
      <span class="dot"></span><span id="statusText">Ready</span>
      <span class="spacer"></span>
      <span id="dims"></span>
    </div>
  </div>`;

const gridHostEl = document.getElementById("gridHost");
const emptyEl = document.getElementById("empty");
const emptyMsgEl = document.getElementById("emptyMsg");
const statusEl = document.getElementById("status");
const statusTextEl = document.getElementById("statusText");
const dimsEl = document.getElementById("dims");
const sourceVarEl = document.getElementById("sourceVar");

// --- Toolbar wiring ---------------------------------------------------------

document.getElementById("addRow").onclick = () => { if (instance) { instance.insertRow(); markDirty(); scheduleCommit(); } };
document.getElementById("delRow").onclick = () => deleteSelectedRows();
document.getElementById("addCol").onclick = () => addColumn();
document.getElementById("commit").onclick = () => commit();
document.getElementById("reload").onclick = () => verso.interact("refresh", {});
document.getElementById("export").onclick = () => exportCsv();

sourceVarEl.addEventListener("change", () => {
  const name = sourceVarEl.value;
  if (name && name !== sourceVar) verso.interact("set-source", { sourceVar: name });
});

// --- Grid construction ------------------------------------------------------

function jssType(type) {
  switch (type) {
    case "numeric": return "numeric";
    case "checkbox": return "checkbox";
    case "calendar": return "calendar";
    default: return "text";
  }
}

function buildGrid() {
  destroyGrid();

  if (!model || !model.columns || model.columns.length === 0) {
    showEmpty();
    return;
  }
  emptyEl.classList.remove("show");

  const columns = model.columns.map((c) => {
    const col = { type: jssType(c.type), title: c.name, width: 130 };
    if (c.type === "calendar") col.options = { format: "YYYY-MM-DD HH:mm:ss" };
    return col;
  });

  instance = jspreadsheet(gridHostEl, {
    data: model.rows.length ? model.rows : [model.columns.map(() => "")],
    columns,
    editable: !readOnly, // a bound DataTable is view-only
    columnSorting: true,
    filters: true,
    allowInsertRow: !readOnly,
    allowDeleteRow: !readOnly,
    allowInsertColumn: false, // structural column edits go through our own toolbar
    allowDeleteColumn: false,
    allowRenameColumn: !readOnly,
    contextMenu: false,
    about: false,
    onafterchanges: onGridEdited,
    oninsertrow: onGridEdited,
    ondeleterow: onGridEdited,
    onsort: onGridEdited,
    onselection: (el, x1, y1, x2, y2) => {
      selRange = [Math.min(y1, y2), Math.max(y1, y2)];
    },
  });

  autoSizeColumns();
  selRange = null;
  updateDims();
}

// Jspreadsheet has no fit-to-content option, so measure header and cell text
// per column and size each to fit (padded and clamped to a sane range).
function autoSizeColumns() {
  if (!instance || !model || !model.columns.length) return;
  const ctx = document.createElement("canvas").getContext("2d");
  const cs = getComputedStyle(gridHostEl);
  ctx.font = `${cs.fontSize} ${cs.fontFamily}`;

  model.columns.forEach((c, x) => {
    let max = ctx.measureText(c.name).width;
    for (const row of model.rows) {
      const v = row[x] == null ? "" : String(row[x]);
      const w = ctx.measureText(v).width;
      if (w > max) max = w;
    }
    instance.setWidth(x, Math.min(Math.max(Math.ceil(max) + 24, 60), 400));
  });
}

function destroyGrid() {
  if (instance) {
    try { instance.destroy(); }
    catch (_) { try { jspreadsheet.destroy(gridHostEl); } catch (_e) {} }
    instance = null;
  }
  gridHostEl.innerHTML = "";
}

function onGridEdited() {
  markDirty();
  updateDims();
  scheduleCommit();
}

// --- Structural edits -------------------------------------------------------

function addColumn() {
  syncRowsFromGrid();
  if (!model) model = { columns: [], rows: [] };
  const name = "col" + (model.columns.length + 1);
  model.columns.push({ name, type: "text" });
  model.rows.forEach((r) => r.push(""));
  if (model.rows.length === 0) model.rows.push(model.columns.map(() => ""));
  buildGrid();
  markDirty();
  scheduleCommit();
}

function deleteSelectedRows() {
  if (!instance) return;
  const data = instance.getData();
  if (data.length === 0) return;

  // Delete the selected row range tracked by onselection; otherwise drop the last row.
  if (selRange) {
    const first = Math.max(0, selRange[0]);
    const count = Math.min(data.length - first, selRange[1] - selRange[0] + 1);
    if (count > 0) instance.deleteRow(first, count);
  } else {
    instance.deleteRow(data.length - 1, 1);
  }
  selRange = null;
  markDirty();
  updateDims();
  scheduleCommit();
}

// --- Commit (frame -> kernel) -----------------------------------------------

function syncRowsFromGrid() {
  if (instance && model) model.rows = instance.getData();
}

function scheduleCommit() {
  if (readOnly) return;
  if (commitTimer) clearTimeout(commitTimer);
  commitTimer = setTimeout(commit, 500);
}

function commit() {
  if (commitTimer) { clearTimeout(commitTimer); commitTimer = 0; }
  if (readOnly) return;
  if (!model || !model.columns || model.columns.length === 0) return;
  syncRowsFromGrid();

  verso.interact("commit", {
    columns: model.columns.map((c) => c.name),
    types: model.columns.map((c) => c.type),
    rows: model.rows,
  });

  dirty = false;
  statusEl.classList.remove("dirty");
  setStatus("Committed to " + sourceVar);
}

function exportCsv() {
  syncRowsFromGrid();
  if (!model || !model.columns.length) return;
  const escape = (v) => {
    const s = v == null ? "" : String(v);
    return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
  };
  const header = model.columns.map((c) => escape(c.name)).join(",");
  const lines = model.rows.map((row) => row.map(escape).join(","));
  const content = [header].concat(lines).join("\n");
  verso.interact("export", { fileName: sourceVar + ".csv", content });
  setStatus("Exported " + sourceVar + ".csv");
}

// --- Incoming data (kernel -> frame) ----------------------------------------

function applyData(payload) {
  if (payload && typeof payload.sourceVar === "string") {
    sourceVar = payload.sourceVar;
  }

  readOnly = !!(payload && payload.readOnly);
  applyReadOnly();
  renderSourceOptions(payload && payload.variables);

  const data = payload && payload.data;
  if (data && Array.isArray(data.columns) && data.columns.length) {
    model = {
      columns: data.columns.map((name, i) => ({ name, type: (data.types && data.types[i]) || "text" })),
      rows: (data.rows || []).map((r) => r.slice()),
    };
    buildGrid();
    setStatus(readOnly ? "Bound to " + sourceVar + " (read-only)" : "Bound to " + sourceVar);
  } else {
    model = null;
    buildGrid();
  }
  dirty = false;
  statusEl.classList.remove("dirty");
}

// Disable the editing controls when the bound source is read-only (a DataTable). Reload, CSV
// export, and the source picker stay enabled.
function applyReadOnly() {
  ["addRow", "addCol", "delRow", "commit"].forEach((id) => {
    const btn = document.getElementById(id);
    if (btn) btn.disabled = readOnly;
  });
}

// --- UI helpers -------------------------------------------------------------

function markDirty() {
  dirty = true;
  statusEl.classList.add("dirty");
  setStatus("Unsaved edits");
}

function setStatus(text) { statusTextEl.textContent = text; }

// Populate the source dropdown with the variable names the host reported (DataBlock or DataTable).
// The bound variable is always selectable even if it is not (yet) a recognized source, so the
// binding stays visible.
function renderSourceOptions(list) {
  const names = Array.isArray(list) ? list.slice() : [];
  if (sourceVar && names.indexOf(sourceVar) === -1) names.unshift(sourceVar);

  sourceVarEl.innerHTML = "";
  if (names.length === 0) {
    const opt = document.createElement("option");
    opt.value = "";
    opt.textContent = "(no data)";
    opt.disabled = true;
    opt.selected = true;
    sourceVarEl.appendChild(opt);
    return;
  }

  names.forEach((name) => {
    const opt = document.createElement("option");
    opt.value = name;
    opt.textContent = name;
    if (name === sourceVar) opt.selected = true;
    sourceVarEl.appendChild(opt);
  });
}

function updateDims() {
  if (instance) {
    const data = instance.getData();
    const cols = model ? model.columns.length : 0;
    dimsEl.textContent = data.length + " rows x " + cols + " cols";
  } else {
    dimsEl.textContent = "";
  }
}

function showEmpty() {
  emptyEl.classList.add("show");
  emptyMsgEl.innerHTML =
    "Assign data to <code>" + escapeHtml(sourceVar) + "</code> in a code cell and run it, " +
    "then pick it from the <b>Data</b> dropdown above.";
  dimsEl.textContent = "";
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

// --- Host message channel ---------------------------------------------------

verso.onMessage((type, payload) => {
  switch (type) {
    case "verso/init":
      if (payload && payload.extension) applyData(payload.extension);
      else buildGrid();
      break;
    case "ext/data":
      applyData(payload);
      break;
    case "verso/themeChanged":
      // The chrome and the grid restyle automatically through --verso-* CSS variables.
      break;
  }
});

verso.ready();
