# Grid Studio

A sample Verso **layout extension** that presents a kernel **DataBlock** as an editable,
Excel-like spreadsheet. It is an *isolated* (iframe) layout: the grid surface runs inside the
host's sandboxed frame, the C# extension owns the binding to a kernel variable, and edits made
in the grid are rebuilt into a real DataBlock the rest of the notebook can query.

It pairs the [Datafication.Core](https://www.nuget.org/packages/Datafication.Core) `DataBlock`
with [Jspreadsheet CE](https://github.com/jspreadsheet/ce) (MIT) for the grid.

## What it shows

- **A DataBlock you can edit by hand.** The grid reads a `DataBlock` from the kernel's variable
  store and renders it. Editing a cell, adding a row, or adding a column commits the result back
  to the same variable as a rebuilt `DataBlock`, so downstream code cells see your edits.
- **Two-way binding over the variable store.** `ILayoutLifecycleHandler.OnRendererMountedAsync`
  seeds the frame with the bound DataBlock and subscribes to variable changes;
  `ILayoutFrameChannel.PostMessageAsync` streams updates in. The frame's commits arrive through
  `ILayoutInteractionHandler` and are written back with `IVariableStore.Set`.
- **Type-aware columns.** The DataBlock schema drives the grid: numeric columns edit as numbers,
  boolean columns as checkboxes, and the rebuilt DataBlock preserves those types.
- **Bundling a third-party grid into an isolated frame.** The renderer is a single
  self-contained module. The grid library and its helper are vendored and injected at runtime,
  with no network access inside the frame.
- **Theme tracking.** The chrome and the grid are themed entirely through the host's `--verso-*`
  CSS variables, so they re-color when the active theme changes.

## How it works

| Piece | Responsibility |
|---|---|
| `GridStudioLayout.cs` | The layout engine, lifecycle handler, and interaction handler. Binds to a kernel variable, streams its DataBlock into the frame, and writes commits back. |
| `DataBlockInterop.cs` | Reads a `DataBlock` into a serializable shape and rebuilds one from edited rows, entirely by reflection. |
| `GridDocument.cs` | The persisted binding (which variable the grid is bound to). |
| `assets/grid.js` | The iframe renderer: a themed toolbar around a Jspreadsheet CE grid, plus the bridge wiring. |
| `assets/vendor/` | The vendored grid library and helper (see Licensing). |
| `RendererScript.cs` | Assembles the single renderer module from the embedded assets at runtime. |

### Data flow

1. The host mounts the iframe and installs the `window.verso` bridge. `grid.js` injects the
   vendored grid library, registers a message handler, and calls `verso.ready()`.
2. The host runs `OnRendererMountedAsync`, which reads the bound `DataBlock` (default variable
   name `data`) and returns its `{ columns, types, rows }` projection as initial state. The host
   delivers it to the frame on `verso/init` under `extension`.
3. When any kernel variable changes, the lifecycle handler re-reads the bound variable and pushes
   the fresh contents; the frame receives them as `ext/data` and rebuilds the grid.
4. Editing the grid sends `verso.interact("commit", { columns, types, rows })`. The interaction
   handler rebuilds a `DataBlock` and writes it back to the bound variable. Re-run a code cell to
   observe the updated DataBlock.

### Reflection, not a project reference

The extension references only `Verso.Abstractions`. It never references Datafication.Core. It
reads and rebuilds the `DataBlock` by reflection, and write-back constructs the new DataBlock
using the *live instance's own assembly* (the one the kernel loaded with `#r "nuget: ..."`). That
keeps the two-way round-trip working regardless of how and where each assembly was loaded.

## Build

```bash
dotnet build samples/showcase/grid-studio/src/Verso.Layout.GridStudio/Verso.Layout.GridStudio.csproj
```

The build produces `Verso.Layout.GridStudio.dll`. Load it like any other Verso extension: drop it
on `verso.extensionsPath`, or load it from a notebook with `#!extension`.

## Try it in a notebook

Open `grid-studio.verso` and follow the steps in its first cell:

1. Run the cell that loads the extension.
2. Run the cell that builds a `DataBlock` named `data`.
3. Switch the layout to **Grid Studio** from the layout picker.
4. Edit cells, add rows or columns, then re-run a code cell to read the committed DataBlock.

Point the grid at a different DataBlock by choosing it from the **DataBlock** dropdown in the
toolbar. The dropdown lists every variable that currently holds a DataBlock and refreshes when
cells run (including Run All) while the layout is open.

## Licensing

This sample is MIT. It bundles two MIT-licensed libraries verbatim under `assets/vendor/`, with
their license texts beside them:

- [Jspreadsheet CE](https://github.com/jspreadsheet/ce) 4.15.0 (`jspreadsheet-ce.LICENSE.txt`)
- [jsuites](https://github.com/jspreadsheet/jsuites) 5.13.5 (`jsuites.LICENSE.txt`)

Only the Community Edition (MIT) of Jspreadsheet is used; the Pro edition is not required.
