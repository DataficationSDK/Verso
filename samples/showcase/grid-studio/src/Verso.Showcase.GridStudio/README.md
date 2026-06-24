# Verso.Showcase.GridStudio

A sample layout extension for [Verso](https://versonotebooks.com) that presents a kernel `DataBlock`
as an editable, Excel-like spreadsheet. It is one of the Verso showcase layouts, built to demonstrate
what the layout system can do.

## What it shows

The grid reads a `DataBlock` from the kernel and renders it. Editing a cell, adding a row, or adding
a column commits the result back to the same variable as a rebuilt `DataBlock`, so downstream code
cells see your edits. Columns are type-aware: numbers edit as numbers, booleans as checkboxes, and
the rebuilt DataBlock preserves those types. The grid runs inside the host's sandboxed frame and
themes itself through the host's `--verso-*` CSS variables, so it follows every Verso theme.

## Installation

Install it from the Verso **Extensions** pane: search for `Verso.Showcase`, install
**Verso.Showcase.GridStudio**, then choose **Grid Studio** from the layout picker. A notebook can
also declare it as a required extension so it installs automatically on open.

## Using it

Build a `DataBlock` variable in a code cell, switch to the Grid Studio layout, and edit it by hand.
Pick a different DataBlock from the toolbar dropdown, which lists every variable that currently holds
one. The extension reads and rebuilds DataBlocks entirely by reflection, so it needs no reference to
Datafication.Core.

## License

MIT. This package bundles [Jspreadsheet CE](https://github.com/jspreadsheet/ce) and
[jsuites](https://github.com/jspreadsheet/jsuites) (both MIT); their license texts are included under
`third-party-licenses/`.
