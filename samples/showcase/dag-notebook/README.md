# DAG Notebook: a notebook that re-runs itself

This sample is a Verso layout extension that turns the familiar notebook view into a
dependency-aware, reactive notebook. Cells that share variables are linked with badge chips, and
running a cell automatically re-runs the cells that depend on it, in dependency order. It is an
*inline* layout: the cells stay the live, editable cell components, and the visual base is a
faithful match of the built-in Notebook layout, so everything DAG-specific reads as an addition
rather than a different product.

The point it quietly makes: **a layout extension can change execution semantics, not just
presentation.** The reactive behavior ships as an installable package; nothing in the host changes.

## How it works

- **Edges are derived from source.** On every render the layout scans each code cell for the
  variables it defines (assignments, functions, type declarations) and the names it references,
  including names inside interpolated strings. A variable with exactly one defining cell links
  that producer to every cell that reads it. The scan is a lightweight heuristic pass (C#, Python,
  and F#), documented as a strong hint rather than a proof.
- **Cross-language edges are read too.** Two of the ways a cell writes a variable are not
  assignments: `#!bind` shares a widget trait as a notebook variable, and `Variables.Get("name")`
  and `Variables.Set("name", ...)` reach the shared store by a name that lives in a string. Both
  are read from the source before comments and strings are stripped, which is what lets a Python
  control link to a C# cell rather than the graph stopping at each language boundary.
- **A control can sit at the top of the graph.** A variable bound to a widget trait changes when
  the control moves, with no cell having run. The layout watches the shared store for exactly
  those variables, treats the cell that bound one as its producer, and cascades from there. Moving
  a slider and finishing a run are the same event as far as the graph is concerned. Readings are
  taken afresh whenever a cell completes, so a cell writing the variable it reads settles instead
  of looping, and a drag is coalesced into one run rather than one per step.
- **Badges are additive chrome.** Each linked cell gets a chip row above its card: `↑ 2 unitPrice`
  reads `unitPrice` from cell 2, `↓ 4 revenue` feeds cell 4, and `↻ threshold` marks a variable
  that follows a control rather than a computation. Chips are numbered by document position but
  tracked by cell id, so they survive reordering. Clicking one scrolls to the linked cell.
- **Completions drive the cascade.** The layout declares the `NotebookEvents` capability, and its
  script relays the host's cell execution events back into the layout's interaction handler. When
  a cell completes successfully and auto-run is on, the layout executes the cell's transitive
  dependents one at a time, in topological order, through the standard notebook operations API.
  The trigger is a completed run, not a keystroke.
- **Stale state is client-side.** The layout embeds its dependency graph as JSON; the script marks
  downstream cells stale (dashed border) the moment a producer starts running and clears the mark
  when each cell re-runs. With auto-run off, stale markers simply wait for you.
- **Ambiguity is surfaced, not guessed at.** A variable assigned in more than one cell produces
  warning chips on every writer and no edges. Cells that form a dependency cycle are flagged and
  their cycle edges excluded, so a cascade can never loop.
- **Batch runs are respected.** The cascade waits a beat before starting; any new cell beginning
  to run inside that window (the host's document-order Run All, for example) cancels the pending
  trigger, so a batch run is never double-executed. The header's **Run DAG** button runs the whole
  notebook in dependency order instead.

## Build

The sample references the local `Verso.Abstractions` project and is built standalone; it is not
part of `Verso.sln`.

```bash
dotnet build src/Verso.Showcase.DagNotebook -c Debug
```

## Run

The published package is **Verso.Showcase.DagNotebook**. Open `dag-notebook.verso` in any Verso
host: it declares the package as a required extension, so the host installs it from NuGet and
opens straight into the layout. You can also install it yourself from the **Extensions** pane
(search `Verso.Showcase`) and switch to **DAG Notebook** from the layout picker.

To run against a local build instead, load the freshly built assembly with
`#!extension ./src/Verso.Showcase.DagNotebook/bin/Debug/net8.0/Verso.Showcase.DagNotebook.dll`
(the path resolves relative to the notebook's folder), then switch to **DAG Notebook**.

The demo notebook seeds a small pricing model: base inputs feed revenue, revenue feeds profit,
and a summary cell reads all three. From there:

- Run everything once with **Run DAG**, then change `unitPrice` in cell 2 and run just that cell:
  revenue, profit, and the summary re-run on their own, in order.
- Toggle **Auto-run dependents: Off** and repeat: the downstream cells mark themselves stale and
  wait.
- The last two cells both assign `counter` on purpose, showing the multi-writer warning chips and
  that ambiguous names are excluded from auto-run.

### The widget demo

`dag-notebook-widget.verso` is the second demo and the shorter one. A Python cell builds an
`ipywidgets` slider, one `#!bind` line shares its value as a notebook variable, and two C# cells
read it. Run **Run DAG** once, then drag the slider and let go: the two C# cells re-run on their
own, in order, with nothing clicked. Turning **Auto-run dependents** off leaves them marked stale
instead. Writing the variable from a cell (`Variables.Set("threshold", 80L);`) moves the slider on
the page, so the link runs in both directions.

The slider is declared with `continuous_update=False`, which holds its value until the handle is
released. With continuous updates the cascade still runs once per gesture rather than once per
step, because a change arriving inside the coalescing window replaces the one before it.

It needs `ipywidgets` in the interpreter Verso is using; the notebook's first Python cell installs
it. Widgets are live in the editor, whether it is served by `verso serve` or opened in VS Code. A
notebook run from the command line has no view to talk to, so there the slider draws from its
saved state and the C# cells simply read whatever value that state holds.

### Starting directly in the layout

`dag-notebook.verso` opens straight into the layout. It declares the package as a required
extension and pins the active layout in `metadata`:

```json
"activeLayout": { "extensionId": "com.verso.showcase.dag-notebook", "layoutId": "dag-notebook" },
"extensions": { "required": ["Verso.Showcase.DagNotebook"] }
```

## Notes

- **Auto-run persists per notebook.** The toggle state round-trips through the layout metadata
  block in the `.verso` file, so a notebook saved with auto-run off stays off.
- **Failures stop the cascade.** If a dependent fails, the cells after it in the cascade never
  run and keep their stale marker; the failed cell gets an error border.
- **The host toolbar is untouched.** Run All keeps its normal document-order behavior; the
  dependency-order run lives in the layout's own header. Both coexist by design.
- **A control that drives a cell which writes it back is a loop the scan cannot see**, because the
  write happens at run time rather than in the source. Taking a fresh reading of every bound
  variable each time a cell completes is what settles it after one pass instead.

## Licensing

This sample is MIT. It bundles no third-party libraries: the stylesheet and script are part of
the sample itself.
