# Verso.Showcase.SlideStudio

A sample layout extension for [Verso](https://versonotebooks.com) that turns a notebook into a
slide deck you author and present from the same view. A filmstrip of cell previews runs down the
left, the selected cell's editor and its output sit side by side behind a draggable splitter, and
a full-screen presenter mode plays the included cells as slides. It is one of the Verso showcase
layouts, built to demonstrate a layout owning serious chrome: panels, a splitter, checkboxes, and
a presenter overlay.

## What it shows

- **Filmstrip.** Every cell appears as a numbered tile with a scaled-down preview of its last
  output (or its source, if it has never run). Clicking a tile loads that cell into the
  workspace. The checkbox in a tile's corner includes or excludes the cell from the presenter
  view, like hiding a slide.
- **Editor and output, split.** The selected cell stays the live, editable cell component, with
  Monaco editing and the run button. Its output renders in the pane on the right, and the
  splitter between them drags.
- **Presenter view.** The Present button plays the included cells full screen, one slide per
  cell, navigated with the arrow keys (Esc exits). Each pane's "on slide" checkbox decides what
  the slide shows: output only by default, source only, or both stacked.

The three checkboxes persist per cell in the notebook file, so the deck travels with it. The same
switches also appear in the cell properties panel under a **Presenter** section; either surface
edits the same state.

## Installation

Install it from the Verso **Extensions** pane: search for `Verso.Showcase`, install
**Verso.Showcase.SlideStudio**, then choose **Slide Studio** from the layout picker. A notebook
can also declare it as a required extension so it installs automatically on open.

## License

MIT. This package bundles no third-party libraries.
