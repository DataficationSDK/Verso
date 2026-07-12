# Slide Studio: author and present a notebook as a slide deck

This sample is a Verso layout extension that presents a notebook the way a slide editor presents
a deck. A filmstrip of cell previews runs down the left; the selected cell's live editor and its
rendered output sit side by side behind a draggable splitter; and a full-screen presenter mode
plays the included cells as slides. It is an *inline* layout: the cell in the editor pane is the
real, editable cell component with Monaco and the run button, not a copy.

The point it quietly makes: **a layout extension can own an entire authoring workflow's chrome**,
with panels, a splitter, per-cell toggles, and a presenter overlay, using only the public layout,
cell interaction, and cell property extension points.

## How it works

- **Three checkboxes decide the deck.** Each filmstrip tile has an include checkbox (unchecking
  it hides the slide, like a hidden slide in a deck), and each workspace pane has an "on slide"
  checkbox choosing whether the slide shows the cell's source, its output, or both stacked. The
  defaults are output only: an audience sees results, and code slides opt in.
- **Flags are cell metadata and mark the notebook dirty.** The layout implements
  `ICellInteractionHandler`, so a checkbox change travels over the cell interaction channel,
  writes a single namespaced key into the cell's metadata, and sets the interaction's
  state-changed signal. That channel carries a dirty flag in every host, which is why the
  checkboxes are not routed through the layout interaction channel: layout metadata persists on
  save, but does not mark the document edited on its own.
- **The same flags appear in the properties panel.** The layout also implements
  `ICellPropertyProvider`, exposing the three switches as a **Presenter** section with toggle
  fields. Both surfaces read and write the same metadata key.
- **The output pane and the slides show the live output, never a copy.** Layout HTML is
  injected via `innerHTML`, so any `<script>` inside a serialized copy of an output would never
  execute, and a chart library that resolves its target element by id would find the copy
  instead of the real output. Slide Studio therefore shows the output element Blazor itself
  rendered: the workspace positions the live cell's output section over the output pane with
  scoped CSS, and each slide's output area is a real cell slot the host's portal fills with the
  live cell (slide CSS strips the editing chrome, the same way the built-in presentation layout
  does). The active cell is the one exception: the editor slot claims it first, so the script
  hands it to its slide while that slide is up and returns it afterwards. Script-driven,
  interactive, and rich outputs behave exactly as they do in the notebook layout. A static,
  script-free fallback copy sits underneath the output pane's overlay for the moments the live
  element does not exist, such as while a markdown cell is being edited (its editor stands in
  for its output) or when a cell's output section is collapsed; slides carry the same fallback
  and switch between the two states with CSS alone.
- **The filmstrip previews are cheap.** A tile renders the cell's first persisted output inside
  a clipped box scaled to a quarter size, with pointer events off; a never-run cell shows a
  source excerpt instead. Because Verso persists outputs in the `.verso` file, the filmstrip is
  meaningful the moment the notebook opens. Script-driven HTML outputs are the exception: a
  tile shows an "Interactive output" placeholder rather than a copy, for the reasons above.
  Diagram outputs are the counter-example that proves the rule: a mermaid copy is only the
  diagram source in the host's container markup, rendered client-side with fresh element ids,
  so tiles and the fallback copies show real rendered diagrams.
- **The presenter deck is prerendered.** Every render also emits the slides as a hidden overlay,
  so presenting and navigating are pure client-side visibility switches with no server round
  trip per keypress.
- **Slides scale like a deck, not a document.** Each slide lays out on a fixed-width canvas
  that the script measures and scales to the screen with a transform (capped, so text does not
  balloon; uncapped downward, so oversized content always fits). Presenting never re-executes
  anything: outputs appear as the cell last rendered them, and geometric scaling makes them
  fill the screen. Re-executing on Present would surprise users whose cells have side effects,
  so it is deliberately off the table; responsive outputs instead get a resize nudge when their
  slide appears, and the canvas refits itself if a chart settles late. Presenting requests
  fullscreen where the host allows it and falls back to a
  viewport-filling overlay where it does not (some webviews); Esc, the browser's own fullscreen
  exit, and the HUD's close button all leave cleanly. Re-renders are deferred while presenting
  and applied on exit.
- **Executions keep everything fresh.** The layout declares the `NotebookEvents` capability and
  requests a debounced re-render when a cell finishes running, so the filmstrip previews and the
  output pane track reality.
- **View state persists with the layout.** The selected cell, splitter ratio, and last presented
  slide round-trip through the layout metadata bucket in the notebook file. The splitter drag is
  fully client-side and posts a single interaction on release.

## Build

The sample references the local `Verso.Abstractions` project and is built standalone; it is not
part of `Verso.sln`.

```bash
dotnet build src/Verso.Showcase.SlideStudio -c Debug
```

Tests live in `tests/Verso.Showcase.SlideStudio.Tests`:

```bash
dotnet test tests/Verso.Showcase.SlideStudio.Tests
```

## Run

The published package is **Verso.Showcase.SlideStudio**. Open `slide-studio.verso` in any Verso
host: it declares the package as a required extension, so the host installs it from NuGet and
opens straight into the layout. You can also install it yourself from the **Extensions** pane
(search `Verso.Showcase`) and switch to **Slide Studio** from the layout picker.

To run against a local build instead, load the freshly built assembly with
`#!extension ./src/Verso.Showcase.SlideStudio/bin/Debug/net8.0/Verso.Showcase.SlideStudio.dll`
(the path resolves relative to the notebook's folder), then switch to **Slide Studio**.

The demo notebook is a small quarterly review deck: a title slide, a results table, a chart, a
code cell that shows both its source and output on one slide, and a scratch cell excluded from
the deck to demonstrate hiding.

## Notes

- The layout declares `CellEdit` and `CellExecute` but not insert, delete, or reorder; a deck is
  arranged, not restructured. Switch to the Notebook layout to add or remove cells.
- Presenter slides show the cell's live rendered output. A cell whose output pane is enabled
  but that has never produced output is skipped rather than shown blank.
- OS-level fullscreen depends on the host granting the Fullscreen API; inside hosts that refuse
  it, presenter view fills the notebook viewport instead.

## Licensing

MIT. The sample bundles no third-party libraries.
