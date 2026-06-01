# Mermaid Diagrams

Verso renders [Mermaid](https://mermaid.js.org) diagrams natively in a dedicated cell type. Flowcharts, sequence diagrams, class diagrams, Gantt charts, and the other Mermaid diagram types render inline alongside your code and data, and the diagram source supports `@variable` substitution so a chart can be driven by values produced in other cells.

## Creating a Mermaid Cell

Mermaid is a cell type, not a magic command. Add a cell and set its type to **Mermaid** from the cell type selector, then write the diagram source directly:

```
graph TD
    A[Start] --> B{Decision}
    B -->|Yes| C[Continue]
    B -->|No| D[Stop]
```

In a `.verso` file the cell is stored with `"type": "mermaid"`. The cell content is the raw diagram text, exactly as you would write it in any Mermaid environment, so existing diagrams paste in unchanged.

Editing a Mermaid cell offers completions for diagram-type keywords (`graph`, `flowchart`, `sequenceDiagram`, `classDiagram`, `stateDiagram-v2`, `erDiagram`, `gantt`, `pie`, `gitgraph`, `mindmap`, `timeline`, and others) and for the variables described below.

## Diagram Types

Any diagram type supported by Mermaid works. Common ones include:

| Keyword | Diagram |
|---------|---------|
| `graph` / `flowchart` | Flowcharts |
| `sequenceDiagram` | Sequence diagrams |
| `classDiagram` | Class diagrams |
| `stateDiagram-v2` | State diagrams |
| `erDiagram` | Entity-relationship diagrams |
| `gantt` | Gantt charts |
| `pie` | Pie charts |
| `gitgraph` | Git commit graphs |
| `mindmap` | Mind maps |
| `timeline` | Timelines |

See the [Mermaid documentation](https://mermaid.js.org/intro/syntax-reference.html) for the full syntax of each.

## Variable Substitution

Mermaid cells participate in the shared variable store, so a diagram can reference values produced in C#, F#, SQL, or any other kernel. Write `@variableName` anywhere in the diagram source and it is replaced with the variable's value before the diagram is rendered.

Set a value in a C# cell:

```csharp
var openOrders = 42;
var status = "Processing";
```

Reference it in a Mermaid cell:

```
graph LR
    A[Orders: @openOrders] --> B[@status]
```

When the diagram renders, `@openOrders` and `@status` are substituted with the current values. If a referenced variable does not exist in the store, the cell reports a warning diagnostic identifying the unresolved name, and hovering a `@variable` reference shows its current type and value.

## Theming

Mermaid manages its own color palette and renders each diagram to an SVG with colors resolved at render time. By default, a diagram uses Mermaid's built-in `default` theme. You can control a diagram's appearance three ways, from quickest to most flexible: a built-in theme, explicit node and class styling, and styling that follows the active Verso theme through CSS variables.

### Built-in themes

Use Mermaid's init directive on the first line of the cell to pick a built-in theme (`default`, `neutral`, `dark`, `forest`, `base`). The directive is passed through to Mermaid unchanged:

```
%%{init: {'theme':'dark'}}%%
graph TD
    A[Start] --> B{Decision}
    B -->|Yes| C[Continue]
    B -->|No| D[Stop]
```

For custom colors, the `base` theme is the one that can be customized through `themeVariables`. The theming engine recognizes hex colors only (`#ff0000`, not `red`):

```
%%{init: {'theme':'base', 'themeVariables': {'primaryColor':'#1f2937', 'primaryTextColor':'#f9fafb', 'lineColor':'#9ca3af'}}}%%
graph TD
    A[Start] --> B[End]
```

Because `@variable` substitution runs over the whole cell, the init directive and the styling
statements below can both reference shared variables. Define a color once in another cell and
reuse it across diagrams:

```csharp
var brand = "#7c3aed";
```

```
%%{init: {'theme':'base', 'themeVariables': {'primaryColor':'@brand'}}}%%
graph TD
    A[Start] --> B[End]
```

### Styling nodes and classes

Style an individual node by id with the `style` statement (`fill`, `stroke`, `stroke-width`, `color`, `stroke-dasharray`):

```
flowchart LR
    A[Start] --> B[End]
    style A fill:#d97706,color:#fff,stroke:#b45309
```

For reusable styling, define a class with `classDef` and apply it with `class`, with the `:::` shorthand, or to every unstyled node at once with the special `default` class:

```
flowchart LR
    A:::accent --> B --> C
    classDef accent fill:#7c3aed,color:#fff,stroke:#5b21b6
    classDef default fill:#e2e8f0,color:#1e293b,stroke:#94a3b8
```

Prefer `style` and `classDef` over an external stylesheet. Mermaid writes its own scoped rules into the diagram, so external CSS targeting the SVG does not apply reliably, whereas `style` and `classDef` values are emitted into the diagram itself.

### Following the active Verso theme

Verso exposes every theme color as a CSS custom property on the page (for example `--verso-accent-primary`, `--verso-cell-output-background`, `--verso-cell-output-foreground`, `--verso-border-default`). To make a diagram follow the active notebook theme, reference those variables from Mermaid's `themeCSS` init option.

`themeCSS` is different from `style`, `classDef`, and `themeVariables`: those are parsed by Mermaid's own grammar, which does not accept `var(...)`. `themeCSS` is injected verbatim into the diagram's `<style>` element, so it is ordinary CSS where `var(--verso-...)` is valid and resolves against the page. Because the custom properties re-resolve when the notebook theme changes, a diagram styled this way recolors with the theme.

```
%%{init: {'themeCSS': '.node rect { fill: var(--verso-accent-primary); stroke: var(--verso-border-default); }'}}%%
flowchart LR
    A[Start] --> B[End]
```

Keep the entire init directive, including the `themeCSS` string, on the first line of the cell. Add rules for whichever parts of the diagram you want to theme, the same way you would write any CSS. The styling is part of the diagram source, so it is preserved when the notebook is exported to HTML.

To keep a palette in one place, combine this with `@variable` substitution: define colors once in another cell and reference them with `@name` inside the init directive or the styling statements, then re-run to repaint.

## Related

- [Mermaid syntax reference](https://mermaid.js.org/intro/syntax-reference.html)
- [Mermaid theme configuration](https://mermaid.js.org/config/theming.html)
