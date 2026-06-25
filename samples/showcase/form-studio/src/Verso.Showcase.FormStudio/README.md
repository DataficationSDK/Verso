# Verso.Showcase.FormStudio

A sample layout extension for [Verso](https://versonotebooks.com) that turns a notebook into a live,
parameterized app. It is one of the Verso showcase layouts, built to demonstrate what the layout
system can do.

## What it shows

Drag input widgets (sliders, dropdowns, toggles, text fields, labels) and charts onto a canvas. Each
input writes a kernel variable; with auto-run on, the notebook recomputes and the charts refresh. The
charts plot a kernel `DataBlock` and let you switch chart type, columns, and colors in the frame with
no round-trip. The dashboard you build is saved with the notebook. The chrome themes itself through
the host's `--verso-*` CSS variables, so it follows every Verso theme.

## Installation

Install it from the Verso **Extensions** pane: search for `Verso.Showcase`, install
**Verso.Showcase.FormStudio**, then choose **Form Studio** from the layout picker. A notebook can
also declare it as a required extension so it installs automatically on open.

## Using it

Build `DataBlock` variables in code cells, switch to the Form Studio layout, and drop widgets onto
the canvas. Bind inputs to your variable names and point a chart at a DataBlock. The extension reads
DataBlocks entirely by reflection, so it needs no reference to Datafication.Core.

## License

MIT. This package bundles [Chart.js](https://github.com/chartjs/Chart.js) (MIT); its license text is
included under `third-party-licenses/`.
