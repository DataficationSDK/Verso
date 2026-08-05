# Verso.Showcase.DagNotebook

A sample layout extension for [Verso](https://versonotebooks.com) that turns the familiar notebook
view into a dependency-aware, reactive notebook. Cells that share variables are linked with badge
chips, and running a cell automatically re-runs the cells that depend on it, in dependency order.
It is one of the Verso showcase layouts, built to demonstrate that the layout system reaches
execution semantics, not just visuals.

## What it shows

The layout looks and behaves like the built-in Notebook layout: the same cards, insert rails, and
drag-to-reorder. On top of that it analyzes each code cell's source to find the variables it
defines and uses, then:

- **Badges** every linked cell. An up-arrow chip such as `↑2 sales` means this cell reads `sales`
  from cell 2; a down-arrow chip such as `↓5 revenue` means cell 5 reads `revenue` from this cell.
  Clicking a chip scrolls to the linked cell.
- **Re-runs dependents.** When a cell finishes running successfully, every cell downstream of it
  re-runs automatically, one at a time, in dependency order. Edit a cell, run it, and watch the
  consequences ripple through the notebook. The trigger is a completed run, not a keystroke.
- **Puts a control at the top of the graph.** A widget trait shared as a notebook variable with
  `#!bind` gets a `↻` chip, and moving the control cascades its dependents exactly as finishing a
  run would. A slider built in a Python cell can drive a computation written in C#, with nothing
  clicked and no cell run by hand.
- **Marks stale cells.** A cell whose upstream producer ran more recently than it did gets a
  dashed border until it re-runs, whether the producer was a cell or a control.
- **Refuses to guess.** A variable assigned in more than one cell gets a warning chip on every
  writer and produces no links, and cells that form a dependency cycle are flagged and excluded
  from automatic execution.

The header bar adds **Run DAG**, which executes the whole notebook in dependency order rather than
document order, and an **Auto-run dependents** toggle for switching the reactive behavior off. The
host toolbar's own Run All keeps its normal document-order behavior.

The dependency analysis is a lightweight, heuristic source scan (C#, Python, and F# assignments,
functions, and type declarations, including names referenced inside interpolated strings). It also
reads the two ways a cell writes a variable without assigning it: `#!bind` directives and
`Variables.Get("name")` and `Variables.Set("name", ...)` calls, which is what carries an edge
across a language boundary. It can over-link or under-link in edge cases, so treat the badges as a
strong hint rather than a proof.

## Installation

Install it from the Verso **Extensions** pane: search for `Verso.Showcase`, install
**Verso.Showcase.DagNotebook**, then choose **DAG Notebook** from the layout picker. A notebook can
also declare it as a required extension so it installs automatically on open.

## License

MIT. This package bundles no third-party libraries.
