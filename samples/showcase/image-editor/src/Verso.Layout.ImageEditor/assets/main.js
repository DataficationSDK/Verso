// Image Studio — the editor frame. Runs inside the host-provided sandboxed iframe.
//
// Contract (same as every isolated Verso layout):
//   - window.verso is installed by the host bridge before this module runs.
//   - verso.onMessage(handler)   delivers host -> frame messages as (type, payload).
//   - verso.ready()              tells the host the frame finished initializing.
//   - verso.interact(type, pl)   sends a layout interaction to the C# extension.
//
// The host writes the active theme to :root as --verso-* custom properties (and re-writes
// them on theme change), so the entire chrome is themed purely through CSS variables and
// tracks every Verso theme — including the ones generated from the active VS Code theme.
// No network is available inside the frame; the document and procedural data arrive over the
// message channel.

const verso = window.verso;

// --- State -----------------------------------------------------------------

let doc = { width: 1024, height: 768, layers: [] };
let procVars = {};        // procedural layer data, keyed by source variable name
let selectedId = null;
let showProps = true;

// --- Chrome (Studio Dark, themed entirely via --verso-* tokens) -------------

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
  .app {
    display: grid;
    grid-template-rows: auto 1fr;
    grid-template-columns: 56px 1fr 288px;
    grid-template-areas: "top top top" "tools stage panel";
    height: 100%;
  }
  /* Top bar */
  .top {
    grid-area: top;
    display: flex; align-items: center; gap: 12px;
    padding: 0 14px; height: 46px;
    background: var(--verso-bg-elevated, #1b1e26);
    border-bottom: 1px solid var(--verso-border-default, #2a2e38);
  }
  .brand { display: flex; align-items: center; gap: 9px; font-weight: 700; letter-spacing: .2px; }
  .brand .mark {
    width: 22px; height: 22px; border-radius: 6px;
    background: var(--verso-accent, #5b8def);
    display: grid; place-items: center; color: #fff;
    box-shadow: 0 2px 8px rgba(0,0,0,.35);
  }
  .brand .mark svg { width: 14px; height: 14px; }
  .brand .sub { color: var(--verso-fg-muted, #9aa0ac); font-weight: 500; font-size: .85em; }
  .top .spacer { flex: 1; }
  .btn {
    appearance: none; cursor: pointer; user-select: none;
    border: 1px solid var(--verso-border-default, #2a2e38);
    background: color-mix(in srgb, var(--verso-fg-default, #fff) 6%, transparent);
    color: var(--verso-fg-default, #e6e6e6);
    border-radius: 8px; padding: 6px 11px; font-size: .92em;
    display: inline-flex; align-items: center; gap: 6px;
    transition: background .12s ease, border-color .12s ease, transform .06s ease;
  }
  .btn:hover { background: color-mix(in srgb, var(--verso-fg-default, #fff) 12%, transparent); }
  .btn:active { transform: translateY(1px); }
  .btn.accent { background: var(--verso-accent, #5b8def); border-color: transparent; color: #fff; }
  .btn.accent:hover { filter: brightness(1.08); background: var(--verso-accent, #5b8def); }
  .btn svg { width: 14px; height: 14px; }

  /* Tool palette */
  .tools {
    grid-area: tools;
    display: flex; flex-direction: column; align-items: center; gap: 6px;
    padding: 10px 0;
    background: var(--verso-bg-elevated, #1b1e26);
    border-right: 1px solid var(--verso-border-default, #2a2e38);
  }
  .tool {
    width: 38px; height: 38px; border-radius: 10px; cursor: pointer;
    display: grid; place-items: center;
    color: var(--verso-fg-muted, #9aa0ac);
    border: 1px solid transparent;
    transition: background .12s ease, color .12s ease;
  }
  .tool:hover { background: color-mix(in srgb, var(--verso-fg-default, #fff) 10%, transparent); color: var(--verso-fg-default, #e6e6e6); }
  .tool.on { color: var(--verso-accent, #5b8def); background: color-mix(in srgb, var(--verso-accent, #5b8def) 16%, transparent); }
  .tool svg { width: 19px; height: 19px; }
  .tools .rule { width: 26px; height: 1px; background: var(--verso-border-default, #2a2e38); margin: 5px 0; }

  /* Canvas stage */
  .stage {
    grid-area: stage;
    position: relative;
    /* min-*: 0 lets this grid item shrink below the canvas size so the viewport can
       scale/scroll instead of the track refusing to shrink and the canvas clipping. */
    min-width: 0; min-height: 0;
    display: grid;
    background:
      radial-gradient(120% 120% at 50% 0%, color-mix(in srgb, var(--verso-fg-default, #fff) 4%, transparent), transparent 60%),
      var(--verso-bg-default, #14161c);
  }
  /* Scroll container for the canvas; the zoom bar lives in .stage so it stays pinned. */
  .viewport {
    overflow: auto;
    display: grid;
    min-width: 0; min-height: 0;
    padding: 28px;
  }
  .canvas-wrap {
    position: relative;
    margin: auto;   /* centers when it fits, scrolls fully when zoomed past the viewport */
    border-radius: 10px;
    box-shadow: 0 18px 48px rgba(0,0,0,.45), 0 0 0 1px var(--verso-border-default, #2a2e38);
    /* Transparency checkerboard, tinted from the theme so it reads on light or dark */
    background-color: var(--verso-bg-elevated, #1b1e26);
    background-image:
      linear-gradient(45deg, color-mix(in srgb, var(--verso-fg-default, #fff) 9%, transparent) 25%, transparent 25%),
      linear-gradient(-45deg, color-mix(in srgb, var(--verso-fg-default, #fff) 9%, transparent) 25%, transparent 25%),
      linear-gradient(45deg, transparent 75%, color-mix(in srgb, var(--verso-fg-default, #fff) 9%, transparent) 75%),
      linear-gradient(-45deg, transparent 75%, color-mix(in srgb, var(--verso-fg-default, #fff) 9%, transparent) 75%);
    background-size: 22px 22px;
    background-position: 0 0, 0 11px, 11px -11px, -11px 0;
  }
  .canvas-wrap canvas { display: block; border-radius: 10px; }
  .zoombar {
    position: absolute; bottom: 12px; left: 14px;
    display: flex; align-items: center; gap: 2px;
    padding: 3px 4px; border-radius: 20px;
    background: color-mix(in srgb, var(--verso-bg-elevated, #000) 80%, transparent);
    border: 1px solid var(--verso-border-default, #2a2e38);
    color: var(--verso-fg-muted, #9aa0ac); font-size: .82em;
    backdrop-filter: blur(6px);
    user-select: none;
  }
  .zbtn {
    appearance: none; cursor: pointer;
    border: none; background: transparent; color: inherit;
    font: inherit; line-height: 1;
    min-width: 22px; height: 22px; padding: 0 6px; border-radius: 14px;
    display: inline-flex; align-items: center; justify-content: center;
    transition: background .12s ease, color .12s ease;
  }
  .zbtn:hover { background: color-mix(in srgb, var(--verso-fg-default, #fff) 12%, transparent); color: var(--verso-fg-default, #e6e6e6); }
  .zbtn.pct { font-variant-numeric: tabular-nums; min-width: 46px; }
  .zsep { width: 1px; height: 16px; background: var(--verso-border-default, #2a2e38); margin: 0 3px; }
  .zdims { padding: 0 6px; font-variant-numeric: tabular-nums; opacity: .8; }

  /* Layers panel */
  .panel {
    grid-area: panel;
    display: flex; flex-direction: column; min-height: 0;
    background: var(--verso-bg-elevated, #1b1e26);
    border-left: 1px solid var(--verso-border-default, #2a2e38);
  }
  .panel h2 {
    margin: 0; padding: 13px 14px 9px;
    font-size: .78em; font-weight: 700; letter-spacing: .12em; text-transform: uppercase;
    color: var(--verso-fg-muted, #9aa0ac);
    display: flex; align-items: center; justify-content: space-between;
  }
  .layers { flex: 1; overflow-y: auto; padding: 0 8px 8px; min-height: 60px; }
  .layer {
    display: flex; align-items: center; gap: 9px;
    padding: 7px 8px; margin: 4px 0; border-radius: 10px;
    border: 1px solid transparent;
    background: color-mix(in srgb, var(--verso-fg-default, #fff) 4%, transparent);
    cursor: pointer; position: relative;
    transition: background .12s ease, border-color .12s ease;
  }
  .layer:hover { background: color-mix(in srgb, var(--verso-fg-default, #fff) 9%, transparent); }
  .layer.sel { border-color: color-mix(in srgb, var(--verso-accent, #5b8def) 60%, transparent); }
  .layer.sel::before {
    content: ""; position: absolute; left: 0; top: 8px; bottom: 8px; width: 3px;
    border-radius: 3px; background: var(--verso-accent, #5b8def);
  }
  .layer.drop-above { box-shadow: inset 0 3px 0 var(--verso-accent, #5b8def); }
  .layer.drop-below { box-shadow: inset 0 -3px 0 var(--verso-accent, #5b8def); }
  .layer.dragging { opacity: .4; }
  .layer .grip { color: var(--verso-fg-muted, #9aa0ac); cursor: grab; opacity: .55; font-size: 1.1em; line-height: 1; touch-action: none; }
  .layer.dragging .grip { cursor: grabbing; }
  .layer .eye {
    width: 24px; height: 24px; border-radius: 6px; display: grid; place-items: center;
    color: var(--verso-fg-default, #e6e6e6); flex: none;
  }
  .layer .eye:hover { background: color-mix(in srgb, var(--verso-fg-default, #fff) 12%, transparent); }
  .layer .eye.off { color: var(--verso-fg-muted, #9aa0ac); opacity: .5; }
  .layer .eye svg { width: 15px; height: 15px; }
  .layer .thumb {
    width: 44px; height: 33px; border-radius: 5px; flex: none;
    box-shadow: 0 0 0 1px var(--verso-border-default, #2a2e38);
    background-color: var(--verso-bg-default, #14161c);
    background-image:
      linear-gradient(45deg, color-mix(in srgb, var(--verso-fg-default, #fff) 10%, transparent) 25%, transparent 25%),
      linear-gradient(-45deg, color-mix(in srgb, var(--verso-fg-default, #fff) 10%, transparent) 25%, transparent 25%);
    background-size: 10px 10px;
  }
  .layer .meta { flex: 1; min-width: 0; }
  .layer .name { font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .layer .kind { font-size: .78em; color: var(--verso-fg-muted, #9aa0ac); text-transform: capitalize; }
  .layer .pct { font-size: .78em; color: var(--verso-fg-muted, #9aa0ac); font-variant-numeric: tabular-nums; }

  /* Properties */
  .props {
    border-top: 1px solid var(--verso-border-default, #2a2e38);
    padding: 11px 13px 15px; overflow-y: auto; max-height: 48%;
  }
  .props.hide { display: none; }
  .props .ptitle { font-size: .78em; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; color: var(--verso-fg-muted, #9aa0ac); margin-bottom: 9px; }
  .field { display: grid; grid-template-columns: 78px 1fr; align-items: center; gap: 8px; margin: 8px 0; }
  .field > label { font-size: .85em; color: var(--verso-fg-muted, #9aa0ac); }
  .field input[type=range] { width: 100%; accent-color: var(--verso-accent, #5b8def); }
  .field input[type=text], .field select {
    width: 100%; padding: 5px 7px; border-radius: 7px;
    border: 1px solid var(--verso-border-default, #2a2e38);
    background: var(--verso-bg-default, #14161c);
    color: var(--verso-fg-default, #e6e6e6); font: inherit;
  }
  .field input[type=color] {
    width: 100%; height: 26px; padding: 0; border-radius: 7px; cursor: pointer;
    border: 1px solid var(--verso-border-default, #2a2e38); background: transparent;
  }
  .empty { color: var(--verso-fg-muted, #9aa0ac); font-size: .88em; padding: 6px 2px; }
`;

// --- Icons (inline SVG; CSP-safe) -------------------------------------------

const ICON = {
  logo: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="3" width="12" height="9" rx="1.5" stroke="currentColor" stroke-width="1.4"/><circle cx="6" cy="6.4" r="1.2" fill="currentColor"/><path d="M3 11l3-3 2 2 3-3 2 2" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  solid: '<svg viewBox="0 0 24 24" fill="none"><rect x="4" y="4" width="16" height="16" rx="2" fill="currentColor"/></svg>',
  gradient: '<svg viewBox="0 0 24 24" fill="none"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="currentColor" stop-opacity=".25"/><stop offset="1" stop-color="currentColor"/></linearGradient></defs><rect x="4" y="4" width="16" height="16" rx="2" fill="url(#g)"/></svg>',
  radial: '<svg viewBox="0 0 24 24" fill="none"><rect x="4" y="4" width="16" height="16" rx="2" stroke="currentColor" stroke-width="1.6"/><circle cx="12" cy="12" r="4.5" fill="currentColor"/></svg>',
  pattern: '<svg viewBox="0 0 24 24" fill="currentColor"><circle cx="7" cy="7" r="1.6"/><circle cx="12" cy="7" r="1.6"/><circle cx="17" cy="7" r="1.6"/><circle cx="7" cy="12" r="1.6"/><circle cx="12" cy="12" r="1.6"/><circle cx="17" cy="12" r="1.6"/><circle cx="7" cy="17" r="1.6"/><circle cx="12" cy="17" r="1.6"/><circle cx="17" cy="17" r="1.6"/></svg>',
  text: '<svg viewBox="0 0 24 24" fill="none"><path d="M5 6h14M12 6v13M9 19h6" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>',
  code: '<svg viewBox="0 0 24 24" fill="none"><path d="M9 8l-4 4 4 4M15 8l4 4-4 4" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  sliders: '<svg viewBox="0 0 24 24" fill="none"><path d="M5 8h9M18 8h1M5 16h1M10 16h9" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/><circle cx="16" cy="8" r="2.2" fill="currentColor"/><circle cx="8" cy="16" r="2.2" fill="currentColor"/></svg>',
  add: '<svg viewBox="0 0 16 16" fill="none"><path d="M8 3v10M3 8h10" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/></svg>',
  download: '<svg viewBox="0 0 16 16" fill="none"><path d="M8 2v8m0 0l-3-3m3 3l3-3M3 13h10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  eye: '<svg viewBox="0 0 24 24" fill="none"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" stroke="currentColor" stroke-width="1.6"/><circle cx="12" cy="12" r="2.6" fill="currentColor"/></svg>',
  eyeOff: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 4l16 16M9.5 9.6A2.6 2.6 0 0012 14.5M6.3 6.4C3.7 8 2 12 2 12s3.5 7 10 7c1.7 0 3.2-.4 4.5-1M11 5.1c.3 0 .7-.1 1-.1 6.5 0 10 7 10 7s-.8 1.6-2.3 3.2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>',
  trash: '<svg viewBox="0 0 16 16" fill="none"><path d="M3 5h10M6.5 5V3.5h3V5M5 5l.5 8h5L11 5" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></svg>',
};

const BLENDS = ["normal", "multiply", "screen", "overlay", "darken", "lighten", "color-dodge", "color-burn", "hard-light", "soft-light", "difference", "exclusion", "hue", "saturation", "color", "luminosity"];

const ADD_TOOLS = [
  { kind: "solid", icon: ICON.solid, label: "Solid color" },
  { kind: "linear-gradient", icon: ICON.gradient, label: "Linear gradient" },
  { kind: "radial-gradient", icon: ICON.radial, label: "Radial gradient" },
  { kind: "dots", icon: ICON.pattern, label: "Pattern" },
  { kind: "text", icon: ICON.text, label: "Text" },
  { kind: "procedural", icon: ICON.code, label: "Procedural (code-driven)" },
];

// --- DOM scaffold -----------------------------------------------------------

document.documentElement.style.height = "100%";
const styleEl = document.createElement("style");
styleEl.textContent = STYLE;
document.head.appendChild(styleEl);

document.body.innerHTML = `
  <div class="app">
    <div class="top">
      <div class="brand"><span class="mark">${ICON.logo}</span> Image Studio <span class="sub">layered compositor</span></div>
      <div class="spacer"></div>
      <button class="btn" id="exportSvgBtn">${ICON.download} Export SVG</button>
      <button class="btn" id="exportBtn">${ICON.download} Export PNG</button>
    </div>
    <div class="tools" id="tools"></div>
    <div class="stage">
      <div class="viewport" id="viewport">
        <div class="canvas-wrap" id="canvasWrap"><canvas id="canvas"></canvas></div>
      </div>
      <div class="zoombar">
        <button class="zbtn" id="zoomOut" title="Zoom out">−</button>
        <button class="zbtn pct" id="zoomPct" title="Reset to 100%">100%</button>
        <button class="zbtn" id="zoomIn" title="Zoom in">+</button>
        <span class="zsep"></span>
        <button class="zbtn" id="zoomFit" title="Fit to window">Fit</button>
        <span class="zdims" id="zoomDims">1024×768</span>
      </div>
    </div>
    <div class="panel">
      <h2>Layers <button class="btn" id="addBtn" title="Add layer">${ICON.add}</button></h2>
      <div class="layers" id="layers"></div>
      <div class="props" id="props"></div>
    </div>
  </div>
`;

const canvas = document.getElementById("canvas");
const canvasWrap = document.getElementById("canvasWrap");
const viewportEl = document.getElementById("viewport");
const layersEl = document.getElementById("layers");
const propsEl = document.getElementById("props");
const toolsEl = document.getElementById("tools");
const zoomPctEl = document.getElementById("zoomPct");
const zoomDimsEl = document.getElementById("zoomDims");

// Tool palette: quick "add layer" buttons + a properties toggle.
for (const t of ADD_TOOLS) {
  const b = el("div", "tool", t.icon);
  b.title = "Add: " + t.label;
  b.onclick = () => verso.interact("add-layer", { kind: t.kind });
  toolsEl.appendChild(b);
}
toolsEl.appendChild(el("div", "rule"));
const propsToggle = el("div", "tool on", ICON.sliders);
propsToggle.title = "Toggle properties";
propsToggle.onclick = () => { showProps = !showProps; propsToggle.classList.toggle("on", showProps); renderProps(); };
toolsEl.appendChild(propsToggle);

document.getElementById("addBtn").onclick = () => verso.interact("add-layer", { kind: "solid" });
document.getElementById("exportBtn").onclick = exportPng;
document.getElementById("exportSvgBtn").onclick = exportSvg;

// Zoom controls: buttons, click-% to reset, Fit, Ctrl/Cmd+wheel, and +/-/0 keys.
document.getElementById("zoomIn").onclick = () => zoomBy(1.25);
document.getElementById("zoomOut").onclick = () => zoomBy(0.8);
document.getElementById("zoomPct").onclick = () => setZoom(1);
document.getElementById("zoomFit").onclick = () => { fitMode = true; viewportEl.scrollLeft = 0; viewportEl.scrollTop = 0; applyZoom(); };

viewportEl.addEventListener("wheel", (e) => {
  if (!(e.ctrlKey || e.metaKey)) return; // plain scroll still pans; modifier+wheel zooms
  e.preventDefault();
  setZoom(currentScale() * Math.exp(-e.deltaY * 0.0015), e.clientX, e.clientY);
}, { passive: false });

document.addEventListener("keydown", (e) => {
  if (e.target && e.target.closest && e.target.closest("input, select, textarea")) return;
  if (e.key === "+" || e.key === "=") { e.preventDefault(); zoomBy(1.25); }
  else if (e.key === "-" || e.key === "_") { e.preventDefault(); zoomBy(0.8); }
  else if (e.key === "0") { e.preventDefault(); setZoom(1); }
});

// --- Rendering --------------------------------------------------------------

function themeVar(name, fallback) {
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return v.length ? v : fallback;
}

function clamp01(v) { return Math.max(0, Math.min(1, v)); }
function num(v, d) { return typeof v === "number" && isFinite(v) ? v : d; }

function mapBlend(b) {
  if (!b || b === "normal") return "source-over";
  return BLENDS.includes(b) ? b : "source-over";
}

function addStops(grad, stops) {
  if (Array.isArray(stops) && stops.length) {
    for (const s of stops) grad.addColorStop(clamp01(num(s.pos, 0)), s.color || "#000");
  } else {
    grad.addColorStop(0, "#000");
    grad.addColorStop(1, "#fff");
  }
}

function drawLayer(ctx, layer, w, h) {
  const p = layer.props || {};
  switch (layer.kind) {
    case "solid": {
      ctx.fillStyle = p.color || "#000";
      ctx.fillRect(0, 0, w, h);
      break;
    }
    case "linear-gradient": {
      const a = num(p.angle, 90) * Math.PI / 180;
      const cx = w / 2, cy = h / 2;
      const dx = Math.cos(a), dy = Math.sin(a);
      const len = Math.abs(w * dx) + Math.abs(h * dy);
      const g = ctx.createLinearGradient(cx - dx * len / 2, cy - dy * len / 2, cx + dx * len / 2, cy + dy * len / 2);
      addStops(g, p.stops);
      ctx.fillStyle = g;
      ctx.fillRect(0, 0, w, h);
      break;
    }
    case "radial-gradient": {
      const cx = num(p.cx, 0.5) * w, cy = num(p.cy, 0.5) * h;
      const r = Math.max(1, num(p.radius, 0.4) * Math.min(w, h));
      const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, r);
      addStops(g, p.stops);
      ctx.fillStyle = g;
      ctx.fillRect(0, 0, w, h);
      break;
    }
    case "checkerboard": {
      const s = Math.max(2, num(p.size, 32) * (w / 1024));
      ctx.fillStyle = p.color1 || "#fff";
      ctx.fillRect(0, 0, w, h);
      ctx.fillStyle = p.color2 || "#ccc";
      for (let y = 0, ry = 0; y < h; y += s, ry++)
        for (let x = 0, rx = 0; x < w; x += s, rx++)
          if ((rx + ry) % 2) ctx.fillRect(x, y, s, s);
      break;
    }
    case "stripes": {
      const wd = Math.max(2, num(p.width, 24) * (w / 1024));
      const a = num(p.angle, 45) * Math.PI / 180;
      ctx.fillStyle = p.color1 || "#222831";
      ctx.fillRect(0, 0, w, h);
      ctx.save();
      ctx.translate(w / 2, h / 2);
      ctx.rotate(a);
      const span = Math.hypot(w, h);
      ctx.fillStyle = p.color2 || "#3a4150";
      for (let x = -span; x < span; x += wd * 2) ctx.fillRect(x, -span, wd, span * 2);
      ctx.restore();
      break;
    }
    case "dots": {
      const s = Math.max(4, num(p.size, 40) * (w / 1024));
      const r = Math.max(0.5, num(p.radius, 3) * (w / 1024));
      ctx.fillStyle = p.color || "#fff";
      for (let y = s / 2; y < h; y += s)
        for (let x = s / 2; x < w; x += s) {
          ctx.beginPath();
          ctx.arc(x, y, r, 0, Math.PI * 2);
          ctx.fill();
        }
      break;
    }
    case "rings": {
      const cx = num(p.cx, 0.5) * w, cy = num(p.cy, 0.5) * h;
      const count = Math.max(1, Math.round(num(p.count, 6)));
      const maxR = Math.min(w, h) / 2;
      ctx.strokeStyle = p.color || "#fff";
      ctx.lineWidth = Math.max(0.5, num(p.width, 2) * (w / 1024));
      for (let i = 1; i <= count; i++) {
        ctx.beginPath();
        ctx.arc(cx, cy, (i / count) * maxR, 0, Math.PI * 2);
        ctx.stroke();
      }
      break;
    }
    case "text": {
      const size = Math.max(6, num(p.size, 0.15) * h);
      ctx.fillStyle = p.color || "#fff";
      ctx.font = `${p.weight || 700} ${size}px ${themeVar("--verso-font-family-sans", "system-ui, sans-serif")}`;
      ctx.textAlign = p.align || "center";
      ctx.textBaseline = "middle";
      ctx.fillText(String(p.text ?? ""), num(p.x, 0.5) * w, num(p.y, 0.5) * h);
      break;
    }
    case "procedural": {
      drawProcedural(ctx, procVars[layer.sourceVar], w, h);
      break;
    }
  }
}

function drawProcedural(ctx, ops, w, h) {
  if (!Array.isArray(ops)) return;
  const m = Math.min(w, h);
  for (const op of ops) {
    if (!op || typeof op !== "object") continue;
    switch (op.op) {
      case "fill":
        ctx.fillStyle = op.color || op.fill || "#000";
        ctx.fillRect(0, 0, w, h);
        break;
      case "rect":
        ctx.fillStyle = op.fill || "#fff";
        ctx.fillRect(num(op.x, 0) * w, num(op.y, 0) * h, num(op.width, 0.1) * w, num(op.height, 0.1) * h);
        break;
      case "circle":
        ctx.fillStyle = op.fill || "#fff";
        ctx.beginPath();
        ctx.arc(num(op.x, 0.5) * w, num(op.y, 0.5) * h, num(op.r, 0.1) * m, 0, Math.PI * 2);
        ctx.fill();
        break;
      case "line":
        ctx.strokeStyle = op.stroke || "#fff";
        ctx.lineWidth = Math.max(0.5, num(op.width, 2) * (w / 1024));
        ctx.beginPath();
        ctx.moveTo(num(op.x1, 0) * w, num(op.y1, 0) * h);
        ctx.lineTo(num(op.x2, 1) * w, num(op.y2, 1) * h);
        ctx.stroke();
        break;
      case "text": {
        const size = Math.max(6, num(op.size, 0.1) * h);
        ctx.fillStyle = op.fill || "#fff";
        ctx.font = `${op.weight || 700} ${size}px ${themeVar("--verso-font-family-sans", "system-ui, sans-serif")}`;
        ctx.textAlign = op.align || "center";
        ctx.textBaseline = "middle";
        ctx.fillText(String(op.text ?? ""), num(op.x, 0.5) * w, num(op.y, 0.5) * h);
        break;
      }
    }
  }
}

function composite(target, scale) {
  const w = Math.round((doc.width || 1024) * scale);
  const h = Math.round((doc.height || 768) * scale);
  target.width = w;
  target.height = h;
  const ctx = target.getContext("2d");
  ctx.clearRect(0, 0, w, h);
  for (const layer of doc.layers || []) {
    if (!layer.visible) continue;
    const off = document.createElement("canvas");
    off.width = w; off.height = h;
    drawLayer(off.getContext("2d"), layer, w, h);
    ctx.globalAlpha = clamp01(num(layer.opacity, 1));
    ctx.globalCompositeOperation = mapBlend(layer.blend);
    ctx.drawImage(off, 0, 0);
  }
  ctx.globalAlpha = 1;
  ctx.globalCompositeOperation = "source-over";
}

function renderCanvas() {
  composite(canvas, 1);
  applyZoom();
}

// --- Zoom -------------------------------------------------------------------

const ZOOM_MIN = 0.1, ZOOM_MAX = 8;
let zoom = 1;        // explicit display scale, used when fitMode is off
let fitMode = true;  // auto-fit the canvas to the viewport (the default)

function clampZoom(s) { return Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, s)); }

// Largest scale (capped at 100%) that fits the document inside the viewport's content box.
function fitScale() {
  const pad = 56; // .viewport padding, both sides
  const availW = viewportEl.clientWidth - pad;
  const availH = viewportEl.clientHeight - pad;
  const s = Math.min(availW / (doc.width || 1), availH / (doc.height || 1));
  return isFinite(s) && s > 0 ? Math.min(s, 1) : 1;
}

function currentScale() { return fitMode ? fitScale() : zoom; }

// Sets the canvas's displayed size from the current scale. The bitmap stays at document
// resolution (composite renders at 1×); the browser scales it for the preview.
function applyZoom() {
  const scale = currentScale();
  canvas.style.width = Math.round((doc.width || 0) * scale) + "px";
  canvas.style.height = Math.round((doc.height || 0) * scale) + "px";
  updateZoom(scale);
}

function updateZoom(scale) {
  const s = scale == null ? currentScale() : scale;
  zoomPctEl.textContent = Math.round(s * 100) + "%";
  zoomDimsEl.textContent = (doc.width || 0) + "×" + (doc.height || 0);
}

// Applies an explicit zoom, keeping the document point under (anchorX, anchorY) fixed by
// nudging the viewport scroll after the relayout. Anchors to the viewport center if omitted.
function setZoom(scale, anchorX, anchorY) {
  const wrap = canvasWrap.getBoundingClientRect();
  const ax = anchorX == null ? wrap.left + wrap.width / 2 : anchorX;
  const ay = anchorY == null ? wrap.top + wrap.height / 2 : anchorY;
  const before = currentScale();
  const docX = (ax - wrap.left) / before;
  const docY = (ay - wrap.top) / before;

  fitMode = false;
  zoom = clampZoom(scale);
  applyZoom();

  const nw = canvasWrap.getBoundingClientRect();
  viewportEl.scrollLeft += (nw.left + docX * zoom) - ax;
  viewportEl.scrollTop += (nw.top + docY * zoom) - ay;
}

function zoomBy(factor) { setZoom(currentScale() * factor); }

// --- Layers panel -----------------------------------------------------------

let dragState = null;

function renderLayers() {
  layersEl.innerHTML = "";
  const ordered = (doc.layers || []).slice().reverse(); // top layer first in the panel
  if (!ordered.length) {
    layersEl.appendChild(el("div", "empty", "No layers yet — add one from the tool palette."));
  }
  for (const layer of ordered) {
    const row = el("div", "layer" + (layer.id === selectedId ? " sel" : ""));
    row.dataset.id = layer.id;

    row.appendChild(el("span", "grip", "⠿"));

    const eye = el("div", "eye" + (layer.visible ? "" : " off"), layer.visible ? ICON.eye : ICON.eyeOff);
    eye.title = layer.visible ? "Hide" : "Show";
    eye.onclick = (e) => { e.stopPropagation(); verso.interact("set-visible", { id: layer.id, visible: !layer.visible }); };
    row.appendChild(eye);

    const thumb = el("canvas", "thumb");
    thumb.width = 44; thumb.height = 33;
    if (layer.visible) {
      const tctx = thumb.getContext("2d");
      tctx.globalAlpha = clamp01(num(layer.opacity, 1));
      drawLayer(tctx, layer, 44, 33);
    }
    row.appendChild(thumb);

    const meta = el("div", "meta");
    meta.appendChild(el("div", "name", escapeHtml(layer.name || "Layer")));
    meta.appendChild(el("div", "kind", (layer.kind || "").replace("-", " ")));
    row.appendChild(meta);

    row.appendChild(el("span", "pct", Math.round(num(layer.opacity, 1) * 100) + "%"));

    wireRow(row);
    layersEl.appendChild(row);
  }
}

// Pointer-based selection + reorder. Native HTML5 drag-and-drop is unreliable inside a
// sandboxed iframe (and some webview hosts disable it), so we drive it with pointer events:
// a click selects the layer; a drag past a small threshold reorders it.
function wireRow(row) {
  row.addEventListener("pointerdown", (e) => {
    if (e.button !== 0) return;
    if (e.target.closest(".eye")) return; // let the visibility toggle handle its own click
    dragState = { id: row.dataset.id, startY: e.clientY, moved: false, target: null };
    try { row.setPointerCapture(e.pointerId); } catch (_) {}
    row.addEventListener("pointermove", onRowMove);
    row.addEventListener("pointerup", onRowUp);
    row.addEventListener("pointercancel", onRowUp);
  });
}

function rowUnder(clientY) {
  let best = null, bestDist = Infinity;
  for (const r of layersEl.querySelectorAll(".layer")) {
    const rect = r.getBoundingClientRect();
    const mid = rect.top + rect.height / 2;
    const d = Math.abs(clientY - mid);
    if (d < bestDist) { bestDist = d; best = { row: r, above: clientY < mid }; }
  }
  return best;
}

function onRowMove(e) {
  if (!dragState) return;
  if (!dragState.moved && Math.abs(e.clientY - dragState.startY) < 4) return;
  dragState.moved = true;

  const src = layersEl.querySelector('.layer[data-id="' + cssEsc(dragState.id) + '"]');
  if (src) src.classList.add("dragging");

  clearDropMarks();
  const hit = rowUnder(e.clientY);
  if (hit && hit.row.dataset.id !== dragState.id) {
    hit.row.classList.add(hit.above ? "drop-above" : "drop-below");
    dragState.target = hit;
  } else {
    dragState.target = null;
  }
}

function onRowUp(e) {
  const row = e.currentTarget;
  row.removeEventListener("pointermove", onRowMove);
  row.removeEventListener("pointerup", onRowUp);
  row.removeEventListener("pointercancel", onRowUp);

  const state = dragState;
  dragState = null;
  clearDropMarks();
  for (const r of layersEl.querySelectorAll(".layer")) r.classList.remove("dragging");
  if (!state) return;

  // No movement → treat as a click that selects the layer.
  if (!state.moved) {
    selectedId = state.id;
    renderLayers();
    renderProps();
    return;
  }

  if (!state.target || state.target.row.dataset.id === state.id) return;

  // Work in panel (top-first) order, then convert back to paint order for the host.
  const display = (doc.layers || []).slice().reverse().map((l) => l.id);
  const from = display.indexOf(state.id);
  if (from < 0) return;
  display.splice(from, 1);
  let to = display.indexOf(state.target.row.dataset.id);
  if (to < 0) return;
  if (!state.target.above) to += 1;
  display.splice(to, 0, state.id);

  verso.interact("reorder", { order: display.slice().reverse() });
}

function clearDropMarks() {
  for (const r of layersEl.querySelectorAll(".layer")) r.classList.remove("drop-above", "drop-below");
}

function cssEsc(s) { return String(s).replace(/["\\]/g, "\\$&"); }

// --- Properties -------------------------------------------------------------

function selectedLayer() { return (doc.layers || []).find((l) => l.id === selectedId) || null; }

function renderProps() {
  propsEl.classList.toggle("hide", !showProps);
  if (!showProps) return;
  // Keep the live control while the user is editing a property. Each edit round-trips through
  // the host, which echoes the document back; rebuilding the panel here would destroy the
  // focused input on every keystroke (and interrupt a slider drag). The next render — on blur,
  // or when another layer is selected — reflects the latest values.
  if (document.activeElement && propsEl.contains(document.activeElement)) return;
  propsEl.innerHTML = "";
  const layer = selectedLayer();
  if (!layer) {
    propsEl.appendChild(el("div", "empty", "Select a layer to edit its properties."));
    return;
  }

  propsEl.appendChild(el("div", "ptitle", "Properties"));

  // Common controls
  propsEl.appendChild(field("Name", textInput(layer.name || "", (v) => verso.interact("rename", { id: layer.id, name: v }))));
  propsEl.appendChild(field("Opacity", range(0, 1, 0.01, num(layer.opacity, 1), (v) => {
    layer.opacity = v; quickRefresh();
    verso.interact("set-opacity", { id: layer.id, opacity: v });
  })));
  propsEl.appendChild(field("Blend", select(BLENDS, layer.blend || "normal", (v) => verso.interact("set-blend", { id: layer.id, blend: v }))));

  const p = layer.props || {};
  const set = (key, value) => { p[key] = value; layer.props = p; quickRefresh(); verso.interact("set-prop", { id: layer.id, key, value }); };
  const setStop = (idx, color) => {
    const stops = Array.isArray(p.stops) ? p.stops.map((s) => ({ pos: num(s.pos, 0), color: s.color })) : [];
    if (!stops[idx]) return;
    stops[idx] = { pos: stops[idx].pos, color };
    set("stops", stops);
  };
  const firstStop = () => (Array.isArray(p.stops) && p.stops[0] ? p.stops[0].color : "#000000");
  const lastStop = () => (Array.isArray(p.stops) && p.stops.length ? p.stops[p.stops.length - 1].color : "#ffffff");

  switch (layer.kind) {
    case "solid":
      propsEl.appendChild(field("Color", color(p.color || "#5b8def", (v) => set("color", v))));
      break;
    case "linear-gradient":
      propsEl.appendChild(field("Angle", range(0, 360, 1, num(p.angle, 90), (v) => set("angle", v))));
      propsEl.appendChild(field("From", color(firstStop(), (v) => setStop(0, v))));
      propsEl.appendChild(field("To", color(lastStop(), (v) => setStop((p.stops || []).length - 1, v))));
      break;
    case "radial-gradient":
      propsEl.appendChild(field("Center X", range(0, 1, 0.01, num(p.cx, 0.5), (v) => set("cx", v))));
      propsEl.appendChild(field("Center Y", range(0, 1, 0.01, num(p.cy, 0.5), (v) => set("cy", v))));
      propsEl.appendChild(field("Radius", range(0.05, 1.2, 0.01, num(p.radius, 0.4), (v) => set("radius", v))));
      propsEl.appendChild(field("Inner", color(firstStop(), (v) => setStop(0, v))));
      propsEl.appendChild(field("Outer", color(lastStop(), (v) => setStop((p.stops || []).length - 1, v))));
      break;
    case "checkerboard":
      propsEl.appendChild(field("Size", range(8, 128, 1, num(p.size, 32), (v) => set("size", v))));
      propsEl.appendChild(field("Color 1", color(p.color1 || "#ffffff", (v) => set("color1", v))));
      propsEl.appendChild(field("Color 2", color(p.color2 || "#cccccc", (v) => set("color2", v))));
      break;
    case "stripes":
      propsEl.appendChild(field("Width", range(4, 96, 1, num(p.width, 24), (v) => set("width", v))));
      propsEl.appendChild(field("Angle", range(0, 360, 1, num(p.angle, 45), (v) => set("angle", v))));
      propsEl.appendChild(field("Color 1", color(p.color1 || "#222831", (v) => set("color1", v))));
      propsEl.appendChild(field("Color 2", color(p.color2 || "#3a4150", (v) => set("color2", v))));
      break;
    case "dots":
      propsEl.appendChild(field("Spacing", range(8, 120, 1, num(p.size, 40), (v) => set("size", v))));
      propsEl.appendChild(field("Radius", range(0.5, 16, 0.5, num(p.radius, 3), (v) => set("radius", v))));
      propsEl.appendChild(field("Color", color(p.color || "#ffffff", (v) => set("color", v))));
      break;
    case "rings":
      propsEl.appendChild(field("Count", range(1, 24, 1, num(p.count, 6), (v) => set("count", v))));
      propsEl.appendChild(field("Width", range(0.5, 12, 0.5, num(p.width, 2), (v) => set("width", v))));
      propsEl.appendChild(field("Center X", range(0, 1, 0.01, num(p.cx, 0.5), (v) => set("cx", v))));
      propsEl.appendChild(field("Center Y", range(0, 1, 0.01, num(p.cy, 0.5), (v) => set("cy", v))));
      propsEl.appendChild(field("Color", color(p.color || "#ffffff", (v) => set("color", v))));
      break;
    case "text":
      propsEl.appendChild(field("Text", textInput(p.text || "", (v) => set("text", v))));
      propsEl.appendChild(field("Size", range(0.04, 0.6, 0.01, num(p.size, 0.15), (v) => set("size", v))));
      propsEl.appendChild(field("Color", color(p.color || "#ffffff", (v) => set("color", v))));
      propsEl.appendChild(field("X", range(0, 1, 0.01, num(p.x, 0.5), (v) => set("x", v))));
      propsEl.appendChild(field("Y", range(0, 1, 0.01, num(p.y, 0.5), (v) => set("y", v))));
      propsEl.appendChild(field("Align", select(["left", "center", "right"], p.align || "center", (v) => set("align", v))));
      break;
    case "procedural":
      propsEl.appendChild(field("Variable", commitInput(layer.sourceVar || "ops", (v) => {
        const name = v.trim() || "ops";
        layer.sourceVar = name;
        verso.interact("set-source", { id: layer.id, sourceVar: name });
      })));
      propsEl.appendChild(el("div", "empty", "Bind to a kernel variable, then run a code cell that assigns it (an array of draw ops). Each procedural layer can use its own variable."));
      break;
  }

  // Delete
  const del = el("button", "btn", ICON.trash + " Delete layer");
  del.style.marginTop = "12px";
  del.onclick = () => verso.interact("remove-layer", { id: layer.id });
  propsEl.appendChild(del);
}

// Repaints canvas + thumbnails from the local doc without a round-trip (keeps sliders smooth).
function quickRefresh() {
  renderCanvas();
  renderLayers();
}

// --- Small DOM builders -----------------------------------------------------

function el(tag, cls, html) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (html != null) e.innerHTML = html;
  return e;
}

function field(label, inputEl) {
  const f = el("div", "field");
  const l = document.createElement("label");
  l.textContent = label;
  f.appendChild(l);
  f.appendChild(inputEl);
  return f;
}

function textInput(value, onChange) {
  const i = document.createElement("input");
  i.type = "text";
  i.value = value;
  i.oninput = () => onChange(i.value);
  return i;
}

// Like textInput but commits only on Enter or blur — used for the variable binding so a
// partially typed name is never sent as a separate interaction.
function commitInput(value, onChange) {
  const i = document.createElement("input");
  i.type = "text";
  i.value = value;
  i.onchange = () => onChange(i.value);
  i.onkeydown = (e) => { if (e.key === "Enter") i.blur(); };
  return i;
}

function range(min, max, step, value, onChange) {
  const i = document.createElement("input");
  i.type = "range";
  i.min = min; i.max = max; i.step = step; i.value = value;
  i.oninput = () => onChange(parseFloat(i.value));
  return i;
}

function color(value, onChange) {
  const i = document.createElement("input");
  i.type = "color";
  i.value = toHex6(value);
  i.oninput = () => onChange(i.value);
  return i;
}

function select(options, value, onChange) {
  const s = document.createElement("select");
  for (const o of options) {
    const opt = document.createElement("option");
    opt.value = o; opt.textContent = o;
    if (o === value) opt.selected = true;
    s.appendChild(opt);
  }
  s.onchange = () => onChange(s.value);
  return s;
}

function toHex6(c) {
  if (typeof c !== "string") return "#000000";
  if (/^#[0-9a-fA-F]{8}$/.test(c)) return c.slice(0, 7);
  if (/^#[0-9a-fA-F]{6}$/.test(c)) return c;
  if (/^#[0-9a-fA-F]{3}$/.test(c)) return "#" + c.slice(1).split("").map((x) => x + x).join("");
  return "#000000";
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
}

// --- Export -----------------------------------------------------------------

function exportPng() {
  const out = document.createElement("canvas");
  composite(out, 1);
  let dataUrl;
  try { dataUrl = out.toDataURL("image/png"); } catch (e) { return; }
  verso.interact("export", { format: "png", dataUrl });
}

// The layers are all vector primitives, so the composite re-emits cleanly as SVG. We render in
// the document's own coordinate space (so the result is resolution-independent) and map per-layer
// opacity + blend straight onto group attributes: the canvas globalCompositeOperation names match
// CSS mix-blend-mode keywords 1:1, with "source-over" becoming "normal".
const SVG_FONT = "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";
let svgDefId = 0;

function exportSvg() {
  verso.interact("export", { format: "svg", svg: buildSvg() });
}

function buildSvg() {
  svgDefId = 0;
  const w = doc.width || 1024, h = doc.height || 768;
  const defs = [];
  const body = [];
  for (const layer of doc.layers || []) {
    if (!layer.visible) continue;
    const inner = svgLayer(layer, w, h, defs);
    if (!inner) continue;
    const attrs = [];
    const op = clamp01(num(layer.opacity, 1));
    if (op < 1) attrs.push(`opacity="${round(op)}"`);
    const blend = mapBlendCss(layer.blend);
    if (blend !== "normal") attrs.push(`style="mix-blend-mode:${blend}"`);
    body.push(`<g${attrs.length ? " " + attrs.join(" ") : ""}>${inner}</g>`);
  }
  return `<?xml version="1.0" encoding="UTF-8"?>\n` +
    `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">` +
    (defs.length ? `<defs>${defs.join("")}</defs>` : "") +
    body.join("") +
    `</svg>`;
}

function mapBlendCss(b) {
  if (!b || b === "normal") return "normal";
  return BLENDS.includes(b) ? b : "normal";
}

function svgLayer(layer, w, h, defs) {
  const p = layer.props || {};
  switch (layer.kind) {
    case "solid":
      return svgRect(0, 0, w, h, p.color || "#000");
    case "linear-gradient": {
      const a = num(p.angle, 90) * Math.PI / 180;
      const cx = w / 2, cy = h / 2, dx = Math.cos(a), dy = Math.sin(a);
      const len = Math.abs(w * dx) + Math.abs(h * dy);
      const id = "g" + (++svgDefId);
      defs.push(`<linearGradient id="${id}" gradientUnits="userSpaceOnUse" ` +
        `x1="${round(cx - dx * len / 2)}" y1="${round(cy - dy * len / 2)}" ` +
        `x2="${round(cx + dx * len / 2)}" y2="${round(cy + dy * len / 2)}">${svgStops(p.stops)}</linearGradient>`);
      return svgRect(0, 0, w, h, `url(#${id})`);
    }
    case "radial-gradient": {
      const cx = num(p.cx, 0.5) * w, cy = num(p.cy, 0.5) * h;
      const r = Math.max(1, num(p.radius, 0.4) * Math.min(w, h));
      const id = "g" + (++svgDefId);
      defs.push(`<radialGradient id="${id}" gradientUnits="userSpaceOnUse" ` +
        `cx="${round(cx)}" cy="${round(cy)}" r="${round(r)}">${svgStops(p.stops)}</radialGradient>`);
      return svgRect(0, 0, w, h, `url(#${id})`);
    }
    case "checkerboard": {
      const s = Math.max(2, num(p.size, 32) * (w / 1024));
      const c1 = colorParts(p.color1 || "#fff"), c2 = colorParts(p.color2 || "#ccc");
      const id = "p" + (++svgDefId);
      defs.push(`<pattern id="${id}" patternUnits="userSpaceOnUse" width="${round(2 * s)}" height="${round(2 * s)}">` +
        `<rect width="${round(2 * s)}" height="${round(2 * s)}" fill="${c1.hex}" fill-opacity="${c1.op}"/>` +
        `<rect width="${round(s)}" height="${round(s)}" fill="${c2.hex}" fill-opacity="${c2.op}"/>` +
        `<rect x="${round(s)}" y="${round(s)}" width="${round(s)}" height="${round(s)}" fill="${c2.hex}" fill-opacity="${c2.op}"/>` +
        `</pattern>`);
      return svgRect(0, 0, w, h, `url(#${id})`);
    }
    case "stripes": {
      const wd = Math.max(2, num(p.width, 24) * (w / 1024));
      const a = num(p.angle, 45);
      const c2 = colorParts(p.color2 || "#3a4150");
      const id = "p" + (++svgDefId);
      defs.push(`<pattern id="${id}" patternUnits="userSpaceOnUse" width="${round(2 * wd)}" height="${round(2 * wd)}" patternTransform="rotate(${round(a)})">` +
        `<rect width="${round(wd)}" height="${round(2 * wd)}" fill="${c2.hex}" fill-opacity="${c2.op}"/></pattern>`);
      return svgRect(0, 0, w, h, p.color1 || "#222831") + svgRect(0, 0, w, h, `url(#${id})`);
    }
    case "dots": {
      const s = Math.max(4, num(p.size, 40) * (w / 1024));
      const r = Math.max(0.5, num(p.radius, 3) * (w / 1024));
      const c = colorParts(p.color || "#fff");
      const id = "p" + (++svgDefId);
      defs.push(`<pattern id="${id}" patternUnits="userSpaceOnUse" width="${round(s)}" height="${round(s)}">` +
        `<circle cx="${round(s / 2)}" cy="${round(s / 2)}" r="${round(r)}" fill="${c.hex}" fill-opacity="${c.op}"/></pattern>`);
      return svgRect(0, 0, w, h, `url(#${id})`);
    }
    case "rings": {
      const cx = num(p.cx, 0.5) * w, cy = num(p.cy, 0.5) * h;
      const count = Math.max(1, Math.round(num(p.count, 6)));
      const maxR = Math.min(w, h) / 2;
      const lw = Math.max(0.5, num(p.width, 2) * (w / 1024));
      const c = colorParts(p.color || "#fff");
      let out = "";
      for (let i = 1; i <= count; i++)
        out += `<circle cx="${round(cx)}" cy="${round(cy)}" r="${round((i / count) * maxR)}" fill="none" stroke="${c.hex}" stroke-opacity="${c.op}" stroke-width="${round(lw)}"/>`;
      return out;
    }
    case "text":
      return svgText(p.text, num(p.x, 0.5) * w, num(p.y, 0.5) * h, Math.max(6, num(p.size, 0.15) * h), p.color || "#fff", p.align || "center", p.weight || 700);
    case "procedural":
      return svgProcedural(procVars[layer.sourceVar], w, h);
  }
  return "";
}

function svgProcedural(ops, w, h) {
  if (!Array.isArray(ops)) return "";
  const m = Math.min(w, h);
  let out = "";
  for (const op of ops) {
    if (!op || typeof op !== "object") continue;
    switch (op.op) {
      case "fill":
        out += svgRect(0, 0, w, h, op.color || op.fill || "#000");
        break;
      case "rect":
        out += svgRect(num(op.x, 0) * w, num(op.y, 0) * h, num(op.width, 0.1) * w, num(op.height, 0.1) * h, op.fill || "#fff");
        break;
      case "circle": {
        const c = colorParts(op.fill || "#fff");
        out += `<circle cx="${round(num(op.x, 0.5) * w)}" cy="${round(num(op.y, 0.5) * h)}" r="${round(num(op.r, 0.1) * m)}" fill="${c.hex}" fill-opacity="${c.op}"/>`;
        break;
      }
      case "line": {
        const c = colorParts(op.stroke || "#fff");
        const lw = Math.max(0.5, num(op.width, 2) * (w / 1024));
        out += `<line x1="${round(num(op.x1, 0) * w)}" y1="${round(num(op.y1, 0) * h)}" x2="${round(num(op.x2, 1) * w)}" y2="${round(num(op.y2, 1) * h)}" stroke="${c.hex}" stroke-opacity="${c.op}" stroke-width="${round(lw)}"/>`;
        break;
      }
      case "text":
        out += svgText(op.text, num(op.x, 0.5) * w, num(op.y, 0.5) * h, Math.max(6, num(op.size, 0.1) * h), op.fill || "#fff", op.align || "center", op.weight || 700);
        break;
    }
  }
  return out;
}

function svgRect(x, y, w, h, fill) {
  if (typeof fill === "string" && fill.startsWith("url("))
    return `<rect x="${round(x)}" y="${round(y)}" width="${round(w)}" height="${round(h)}" fill="${fill}"/>`;
  const c = colorParts(fill);
  return `<rect x="${round(x)}" y="${round(y)}" width="${round(w)}" height="${round(h)}" fill="${c.hex}" fill-opacity="${c.op}"/>`;
}

function svgText(text, x, y, size, fill, align, weight) {
  const anchor = align === "left" ? "start" : align === "right" ? "end" : "middle";
  const c = colorParts(fill);
  return `<text x="${round(x)}" y="${round(y)}" font-family="${SVG_FONT}" font-size="${round(size)}" font-weight="${weight}" ` +
    `fill="${c.hex}" fill-opacity="${c.op}" text-anchor="${anchor}" dominant-baseline="central">${xmlEsc(String(text ?? ""))}</text>`;
}

function svgStops(stops) {
  const list = (Array.isArray(stops) && stops.length) ? stops : [{ pos: 0, color: "#000" }, { pos: 1, color: "#fff" }];
  return list.map((s) => {
    const c = colorParts(s.color || "#000");
    return `<stop offset="${round(clamp01(num(s.pos, 0)))}" stop-color="${c.hex}" stop-opacity="${c.op}"/>`;
  }).join("");
}

// Splits a CSS color into a 6-digit hex + separate opacity, since SVG presentation attributes
// don't accept #RRGGBBAA. Named colors and 3/6-digit hex pass through at full opacity.
function colorParts(c) {
  if (typeof c !== "string") return { hex: "#000000", op: 1 };
  const s = c.trim();
  if (/^#[0-9a-fA-F]{8}$/.test(s)) return { hex: s.slice(0, 7), op: round(parseInt(s.slice(7, 9), 16) / 255) };
  if (/^#[0-9a-fA-F]{4}$/.test(s)) {
    return { hex: "#" + s[1] + s[1] + s[2] + s[2] + s[3] + s[3], op: round(parseInt(s[4] + s[4], 16) / 255) };
  }
  return { hex: s, op: 1 };
}

function round(n) { return Math.round(n * 100) / 100; }

function xmlEsc(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&apos;" }[c]));
}

// --- Full render ------------------------------------------------------------

function renderAll() {
  renderCanvas();
  renderLayers();
  renderProps();
}

function setDocument(next) {
  if (!next || typeof next !== "object") return;
  doc = { width: num(next.width, 1024), height: num(next.height, 768), layers: Array.isArray(next.layers) ? next.layers : [] };
  if (selectedId && !doc.layers.some((l) => l.id === selectedId)) selectedId = null;
  if (!selectedId && doc.layers.length) selectedId = doc.layers[doc.layers.length - 1].id;
  renderAll();
}

// --- Host message channel ---------------------------------------------------

verso.onMessage((type, payload) => {
  switch (type) {
    case "verso/init":
      if (payload && payload.extension) {
        if (payload.extension.vars) procVars = payload.extension.vars;
        setDocument(payload.extension.document);
      } else {
        renderAll();
      }
      break;
    case "ext/document":
      if (payload && payload.document) setDocument(payload.document);
      break;
    case "ext/vars":
      if (payload && payload.vars) {
        procVars = Object.assign({}, procVars, payload.vars);
        renderCanvas();
        renderLayers();
      }
      break;
    case "verso/themeChanged":
      // Chrome restyles via CSS automatically; repaint the canvas (theme-derived text/stroke).
      renderCanvas();
      break;
  }
});

// Re-fit (or just refresh the readout) whenever the viewport changes size. A ResizeObserver
// catches host panel resizes that never fire a window 'resize' in the iframe — the case where
// the side panels reflowed but the canvas kept clipping instead of scaling.
function onViewportResize() { if (fitMode) applyZoom(); else updateZoom(); }
window.addEventListener("resize", onViewportResize);
if (typeof ResizeObserver === "function") {
  new ResizeObserver(onViewportResize).observe(viewportEl);
}

renderAll();
verso.ready();
