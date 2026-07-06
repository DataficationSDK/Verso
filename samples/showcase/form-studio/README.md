# Form Studio

A sample Verso **layout extension** that turns a notebook into a live, parameterized app. It is an
*isolated* (iframe) layout: the canvas runs inside the host's sandboxed frame, you drag input
widgets and charts onto it, and the inputs write **kernel variables** that downstream code cells
react to. The same notebook is both an editable document and a small dashboard you can drive.

It pairs kernel data with [Chart.js](https://www.chartjs.org) (MIT) for the charts. A chart can
plot a [Datafication.Core](https://www.nuget.org/packages/Datafication.Core) `DataBlock` or a
`System.Data.DataTable` (such as the results a SQL cell shares to the variable store).

## What it shows

- **A dashboard you build by dragging.** Drop sliders, dropdowns, toggles, text fields, labels, and
  charts onto a canvas. Move and resize them in Edit mode; use them in Preview mode. The canvas you
  build is the layout's document.
- **Inputs that write kernel variables.** Each input binds to a kernel variable by name and writes
  it on change through `ILayoutInteractionHandler`. With Auto-run on, the layout re-runs the
  notebook so downstream cells recompute, then pushes fresh data back to the charts.
- **Charts bound to kernel data.** A chart widget reads a kernel `DataBlock` or
  `System.Data.DataTable` and plots it. Switch the chart type (bar, line, pie, doughnut, scatter),
  pick the X and Y columns, and recolor the series. The visual controls update instantly in the
  frame with no kernel round-trip.
- **Bundling a third-party chart library into an isolated frame.** The renderer is a single
  self-contained module. Chart.js is vendored and injected at runtime, with no network access
  inside the frame.
- **Theme tracking.** The chrome is themed entirely through the host's `--verso-*` CSS variables.
  Chart.js paints to a canvas, so the tokens are read into the chart options and the charts repaint
  when the active theme changes.

## How it works

| Piece | Responsibility |
|---|---|
| `FormStudioLayout.cs` | The layout engine, lifecycle handler, and interaction handler. Seeds the frame, writes input values to kernel variables, re-runs the notebook, and pushes chart data back. |
| `FormDocument.cs` | The persisted canvas (every widget plus the auto-run flag), kept as the frame's own JSON and parsed for the parts the C# side acts on. |
| `DataBlockReader.cs` | Reads a chart source into a serializable `{ columns, types, rows }` shape: a `DataBlock` entirely by reflection, a `DataTable` through its typed API. |
| `assets/form.js` | The iframe renderer: palette, drag-and-drop canvas, properties panel, Chart.js wiring, and the bridge. |
| `assets/vendor/` | The vendored chart library (see Licensing). |
| `RendererScript.cs` | Assembles the single renderer module from the embedded assets at runtime. |

### Data flow

1. The host mounts the iframe and installs the `window.verso` bridge. `form.js` injects Chart.js,
   registers a message handler, and calls `verso.ready()`.
2. The host runs `OnRendererMountedAsync`, which returns the saved canvas document and the list of
   chart source variables (DataBlocks and DataTables) as initial state. The host delivers it to
   the frame on `verso/init`.
3. Moving an input sends `verso.interact("set-value", { var, kind, value })`. The interaction
   handler writes the value to the bound kernel variable with `IVariableStore.Set`, then (if
   Auto-run is on) re-runs the notebook.
4. Running cells raises the variable-store change event. The lifecycle handler re-reads each chart's
   bound source and pushes it as `ext/chart-data`; the frame updates the matching chart.
5. Structural changes (adding, moving, or configuring a widget) send `verso.interact("save-doc", ...)`,
   which the host persists through layout metadata so the dashboard is restored on reopen.

### The recompute loop

The notebook's compute cell reads the dashboard's inputs with `Variables.Get<T>("name")` and rebuilds
the DataBlock the chart plots. Reading through `Variables` (the shared store exposed to C# cells)
means the cell always sees the live value the dashboard wrote, and `Get<T>` returns a sensible
default before any widget is bound, so the cell also runs correctly on its own. Auto-run uses the
host's "execute all" operation and is debounced, so dragging a slider triggers one recompute rather
than dozens. Turn Auto-run off to drive recomputes only with the **Run** button.

### Reflection, not a project reference

The extension references only `Verso.Abstractions`. It never references Datafication.Core. It reads
a `DataBlock` by reflection against whichever Core assembly the kernel loaded with
`#r "nuget: ..."`, so the charts work regardless of how that assembly was loaded. A `DataTable` is
a BCL type and is read through its strongly-typed API, no reflection needed. Form Studio only reads
its chart sources; its inputs write plain scalar variables, so there is no write-back of structured
data here (that is Grid Studio's story).

## Build

```bash
dotnet build samples/showcase/form-studio/src/Verso.Showcase.FormStudio/Verso.Showcase.FormStudio.csproj
```

The build produces `Verso.Showcase.FormStudio.dll`. Load it like any other Verso extension: drop it on
`verso.extensionsPath`, or load it from a notebook with `#!extension`. The published package is
**Verso.Showcase.FormStudio**, installable from the Extensions pane.

## Try it in a notebook

Open `form-studio.verso`. It declares **Verso.Showcase.FormStudio** as a required extension, so the
host installs it from NuGet on open. Then:

1. Run the cells that build the `sales` and `chartData` DataBlocks.
2. Switch the layout to **Form Studio** from the layout picker.
3. Drag a Slider, a Dropdown, and a Bar chart onto the canvas. Bind the slider to `minUnits`, the
   dropdown to `region` (options `All, North, South, East, West`), and point the chart at
   `chartData` with X axis `Month` and Y axis `Units`.
4. Click **Preview**, then move the slider or change the region and watch the chart update.

## Licensing

This sample is MIT. It bundles one MIT-licensed library verbatim under `assets/vendor/`, with its
license text beside it:

- [Chart.js](https://github.com/chartjs/Chart.js) 4.4.6 (`chart.LICENSE.txt`), the auto-registering
  UMD build.
