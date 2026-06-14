// Sparkline renderer — runs inside the host-provided sandboxed iframe.
//
// Contract (see docs/extensions/layouts.md, "Isolated (iframe) layouts"):
//   - window.verso is installed by the host bridge before this module runs.
//   - verso.onMessage(handler)  delivers host -> frame messages as (type, payload).
//   - verso.ready()             tells the host the frame finished initializing.
//   - verso.interact(type, pl)  sends a layout interaction to the C# extension.
//
// The host applies the active theme tokens to the frame's :root as --verso-*
// custom properties and re-applies them on theme change, so we read colors from
// getComputedStyle and repaint. No network access is available inside the frame;
// all data arrives over the message channel.

const verso = window.verso;

let values = [];
let selectedIndex = -1;

// The host mounts this module in a bare iframe document, so <html>/<body> default
// to content-based (auto) height. A canvas sized "height:100%" would resolve that
// against a zero-height body and collapse, drawing nothing. Make the document fill
// the iframe viewport so the canvas has a real box to paint into.
document.documentElement.style.height = "100%";
document.body.style.height = "100%";
document.body.style.margin = "0";
document.body.style.overflow = "hidden";

const canvas = document.createElement("canvas");
canvas.style.display = "block";
canvas.style.width = "100%";
canvas.style.height = "100%";
document.body.appendChild(canvas);

function themeVar(name, fallback) {
  const resolved = getComputedStyle(document.documentElement)
    .getPropertyValue(name)
    .trim();
  return resolved.length > 0 ? resolved : fallback;
}

// Maps each value to an {x, y} pixel position within a padded plot area.
function pointPositions(width, height) {
  const pad = 12;
  const innerW = Math.max(1, width - pad * 2);
  const innerH = Math.max(1, height - pad * 2);
  const n = values.length;
  if (n === 0) return [];

  let min = Math.min.apply(null, values);
  let max = Math.max.apply(null, values);
  if (min === max) {
    min -= 1;
    max += 1;
  }

  const stepX = n > 1 ? innerW / (n - 1) : 0;
  const points = [];
  for (let i = 0; i < n; i++) {
    const x = n > 1 ? pad + i * stepX : pad + innerW / 2;
    const t = (values[i] - min) / (max - min);
    const y = pad + innerH - t * innerH;
    points.push({ x, y });
  }
  return points;
}

function draw() {
  const dpr = window.devicePixelRatio || 1;
  const rect = canvas.getBoundingClientRect();
  const width = Math.max(1, Math.floor(rect.width));
  const height = Math.max(1, Math.floor(rect.height));

  canvas.width = Math.floor(width * dpr);
  canvas.height = Math.floor(height * dpr);

  const ctx = canvas.getContext("2d");
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

  const bg = themeVar("--verso-bg-default", "#ffffff");
  const accent = themeVar("--verso-accent", "#0078d4");
  const muted = themeVar("--verso-fg-muted", "#888888");
  const fg = themeVar("--verso-fg-default", "#1e1e1e");
  const sans = themeVar("--verso-font-family-sans", "sans-serif");

  ctx.fillStyle = bg;
  ctx.fillRect(0, 0, width, height);

  const points = pointPositions(width, height);
  if (points.length === 0) {
    ctx.fillStyle = muted;
    ctx.font = "13px " + sans;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillText("Waiting for data…", width / 2, height / 2);
    return;
  }

  // Baseline along the bottom of the plot area.
  ctx.strokeStyle = muted;
  ctx.globalAlpha = 0.4;
  ctx.beginPath();
  ctx.moveTo(12, height - 12);
  ctx.lineTo(width - 12, height - 12);
  ctx.stroke();
  ctx.globalAlpha = 1;

  // The sparkline itself.
  ctx.strokeStyle = accent;
  ctx.lineWidth = 2;
  ctx.lineJoin = "round";
  ctx.beginPath();
  for (let i = 0; i < points.length; i++) {
    const p = points[i];
    if (i === 0) ctx.moveTo(p.x, p.y);
    else ctx.lineTo(p.x, p.y);
  }
  ctx.stroke();

  // Data points; the selected point is emphasized in the foreground color.
  for (let i = 0; i < points.length; i++) {
    const p = points[i];
    const isSelected = i === selectedIndex;
    ctx.fillStyle = isSelected ? fg : accent;
    ctx.beginPath();
    ctx.arc(p.x, p.y, isSelected ? 4.5 : 2.5, 0, Math.PI * 2);
    ctx.fill();
  }
}

function nearestIndex(clientX) {
  const rect = canvas.getBoundingClientRect();
  const points = pointPositions(Math.floor(rect.width), Math.floor(rect.height));
  if (points.length === 0) return -1;

  const x = clientX - rect.left;
  let best = 0;
  let bestDistance = Infinity;
  for (let i = 0; i < points.length; i++) {
    const distance = Math.abs(points[i].x - x);
    if (distance < bestDistance) {
      bestDistance = distance;
      best = i;
    }
  }
  return best;
}

function setValues(next) {
  values = Array.isArray(next)
    ? next.map(Number).filter((v) => !Number.isNaN(v))
    : [];
  if (selectedIndex >= values.length) selectedIndex = -1;
  draw();
}

canvas.addEventListener("click", (event) => {
  const index = nearestIndex(event.clientX);
  if (index < 0) return;

  selectedIndex = index;
  draw();

  // Report the selection to the C# interaction handler, which writes it to a
  // kernel variable. The host stamps the layout identity onto the message.
  verso.interact("select-point", { index, value: values[index] });
});

verso.onMessage((type, payload) => {
  switch (type) {
    case "verso/init":
      // Initial values come from the lifecycle handler's mount payload, delivered
      // on the init message under `extension`.
      if (payload && payload.extension && Array.isArray(payload.extension.values)) {
        setValues(payload.extension.values);
      } else {
        draw();
      }
      break;

    case "ext/data":
      // Live push from ILayoutFrameChannel.PostMessageAsync("data", { values }).
      if (payload && Array.isArray(payload.values)) {
        setValues(payload.values);
      }
      break;

    case "verso/themeChanged":
      // The bridge already wrote the new tokens to :root; just repaint.
      draw();
      break;
  }
});

window.addEventListener("resize", draw);

draw();

// Tell the host the frame is initialized. The bridge re-announces this until the
// host responds, so a single call is enough even if the host's listener is not yet
// attached when this runs.
verso.ready();
