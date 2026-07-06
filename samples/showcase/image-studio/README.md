# Image Studio: a notebook that looks like an image editor

This sample is a Verso layout extension that turns a notebook into a layered image
compositor: a canvas in the middle, a Photoshop-style layer panel on the right, and a tool
palette on the left. The whole editor runs inside the host's sandboxed iframe (an *isolated*
layout), draws with the HTML5 canvas, and is themed entirely through the host's `--verso-*`
CSS variables, so it adapts to every Verso theme, including the ones generated from your
active VS Code theme.

The point it quietly makes: **the document and the editor are the same `.verso` file viewed
two ways.** Open it in the Notebook layout and it is a notebook; switch to Image Studio and it
is an image editor. Nothing about the file changes.

## How it works

- **The layer stack is the layout's document.** It is persisted into the notebook's `layouts`
  block through the standard `ILayoutEngine.GetLayoutMetadata()` / `ApplyLayoutMetadata()`
  round-trip: no host changes, just metadata.
- **The frame owns the editor.** The C# side holds the document and streams it to the frame;
  the frame (`assets/main.js`) renders the canvas, the layer panel, and the properties, and
  sends edits back as layout interactions (`add-layer`, `reorder`, `set-opacity`,
  `set-blend`, `set-prop`, …).
- **Any layer can be code.** A *procedural* layer defers its drawing to a kernel variable. The
  layout subscribes to the variable store and pushes the variable's value into the live frame
  whenever it changes, so editing and re-running a code cell repaints that layer immediately.

Layer kinds: solid, linear gradient, radial gradient, checkerboard, stripes, dots, rings,
text, and procedural. Each layer has its own opacity and a full set of canvas blend modes
(multiply, screen, overlay, …).

## Build

The sample references the local `Verso.Abstractions` project and is built standalone; it is not
part of `Verso.sln`.

```bash
dotnet build src/Verso.Showcase.ImageStudio -c Debug
```

## Run

The published package is **Verso.Showcase.ImageStudio**. Open `image-studio.verso` in any Verso host:
it declares the package as a required extension, so the host installs it from NuGet and opens straight
into the layout. You can also install it yourself from the **Extensions** pane (search
`Verso.Showcase`) and switch to **Image Studio** from the layout picker.

To run against a local build instead, load the freshly built assembly with
`#!extension ./src/Verso.Showcase.ImageStudio/bin/Debug/net8.0/Verso.Showcase.ImageStudio.dll`
(the path resolves relative to the notebook's folder), then switch to **Image Studio**.

You will land on a seeded composition (a sunset gradient, a soft sun, a dot grid, and a
title). From there:

- Toggle a layer's visibility, drag its opacity, change its blend mode, or drag rows to
  reorder: the canvas recomposites live.
- Add layers from the tool palette; edit their colors, angles, and positions in the
  properties panel.
- Add a **Procedural** layer (the `</>` icon), then run the second code cell to drive it from
  the `ops` variable. Edit the instructions and re-run to repaint it live.
- **Save** the notebook: the layer stack is written into the `.verso` file and restored the
  next time you open the notebook in this layout.

### Starting directly in the layout

`image-studio.verso` already opens straight into the editor. It declares the package as a required
extension and pins the active layout in `metadata`:

```json
"activeLayout": { "extensionId": "com.verso.showcase.image-studio", "layoutId": "image-studio" },
"extensions": { "required": ["Verso.Showcase.ImageStudio"] }
```

The required extension loads before the first render, so the notebook paints in Image Studio on open
with no layout switch.

## Notes

- **Saving:** edits made through the editor are captured when the notebook is saved (the host
  reads the layout's metadata at save time).
- **Zoom:** the bar at the bottom-left of the stage zooms the canvas: the `−` / `＋` buttons,
  Ctrl/Cmd + mouse wheel, or the `+` / `-` / `0` keys. "Fit" auto-scales the canvas to the
  viewport and keeps it fitted as the window or surrounding panels resize; click the percentage
  to reset to 100%. When zoomed past the viewport the stage scrolls (plain wheel pans).
- **Export:** while Image Studio is the active layout it contributes **PNG Image** and **SVG
  Image** to the host's **Export** menu, alongside the notebook's other export formats. They are
  layout-scoped, so they appear only in this layout and are absent everywhere else. **PNG Image**
  asks the host to deliver the rasterized composite as a download. **SVG Image** delivers the same
  composite as vector markup instead: because every layer is a vector primitive, the frame
  re-emits the stack as SVG in the document's own coordinate space, so it stays crisp at any size.
  Per-layer opacity maps to a group `opacity` and blend modes to CSS `mix-blend-mode`. Text uses a
  generic system font fallback (the theme font is not embedded), and `mix-blend-mode` renders in
  browsers though some standalone SVG tools support it only partially. Hosts that do not support
  file downloads from a layout simply ignore either request.

  The export buttons live in the host chrome rather than the frame: the menu action asks the
  isolated frame to produce the bytes (only the frame can rasterize the canvas), and the frame
  streams them back for the host to download. It is a small example of an isolated layout
  contributing actions to the host toolbar through the `IToolbarAction` extension point.

## Licensing

This sample is MIT. It bundles no third-party libraries: the only asset is `assets/main.js`,
which is part of the sample itself.
