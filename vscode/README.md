# Verso Notebook

**Interactive .NET notebooks for VS Code with C#, F#, JavaScript, TypeScript, PowerShell, Python, SQL, and more.**

![Verso in action](https://datafication.co/assets/verso/RunningVersoNotebook.gif)

## Features

- **C#, F#, PowerShell, and Python with IntelliSense** including completions, diagnostics, hover info, and NuGet package references
- **JavaScript and TypeScript** with Node.js execution, npm package management via `#!npm`, and a pure .NET Jint fallback for environments without Node.js
- **SQL database connectivity** with paginated results, schema inspection, and EF Core scaffolding
- **HTTP requests** using `.http` file syntax with variable interpolation, dynamic variables, named request chaining, and cross-kernel response sharing
- **Markdown, HTML, and Mermaid** cells for documentation, visualizations, and diagrams
- **Variable sharing** across languages so you can set a value in C# and use it in SQL, F#, or PowerShell
- **Typed notebook parameters** that turn a notebook into a reusable template, filled in from a form in the editor or `--param` flags on the CLI
- **Notebook, Dashboard, and Presentation layouts** to view cells as a linear document, arrange them in a drag-and-drop grid, or present read-only results
- **Notebook comparison** against the last saved file, git HEAD, any ref, or another notebook file, with cell-level diffs, moved-cell detection, and side-by-side rendered output comparison
- **GitHub Copilot integration** with a `@verso` chat participant and agent-mode tools that let Copilot create, edit, and run cells for you
- **In-app extension marketplace** to search NuGet and install new kernels, layouts, and themes without leaving the notebook
- **Light, Dark, and High Contrast themes** that are hot-swappable at runtime (High Contrast meets WCAG 2.1 AA)
- **Import Jupyter (.ipynb) and Polyglot (.dib) notebooks** with automatic conversion of magic commands and cell types
- **Export to HTML or Markdown** from the editor's Export menu or headlessly with the Verso CLI
- **Fully extensible** with a public API for adding new languages, cell types, themes, layouts, and more

## Writing Code with IntelliSense

Verso's C# kernel is powered by Roslyn, giving you the latest language features, persistent state across cells, real-time error checking, and code completions as you type. The F# kernel offers the same experience powered by FSharp.Compiler.Service. The PowerShell kernel hosts a persistent runspace with full cmdlet support, pipeline-aware output, and completions powered by `CommandCompletion`. The Python kernel embeds CPython via pythonnet with IntelliSense powered by jedi, bidirectional variable sharing with other kernels, and virtual environment support via `#!pip`.

## JavaScript and TypeScript

The JavaScript kernel runs cells in a persistent Node.js subprocess with full access to `require()`, dynamic `import()`, and top-level `await`. Variables declared with `var` or assigned to `globalThis` persist across cells and are shared with other language kernels. Install npm packages directly from a cell with the `#!npm` magic command:

```javascript
#!npm lodash
```

```javascript
const _ = require('lodash');
console.log(_.capitalize('hello world'));
```

TypeScript cells are automatically transpiled using the TypeScript compiler API and share the same Node.js execution environment and variable scope as JavaScript. The `typescript` module is auto-installed on first use.

In environments where Node.js is not installed, the JavaScript kernel falls back to Jint, a pure .NET ES2024 interpreter that requires no external dependencies. TypeScript requires Node.js and is not available in Jint mode.

![C# IntelliSense in Verso](https://datafication.co/assets/verso/IntellisenseVerso.gif)

## Notebook, Dashboard, and Presentation Layouts

Every notebook can be viewed in three ways. **Notebook layout** presents cells in a familiar top-to-bottom flow. **Dashboard layout** lets you drag and resize cells on a 12-column grid to build interactive dashboards. **Presentation layout** turns the notebook into a read-only, output-focused flow for walking through results. The active layout is saved in the `.verso` file and you can switch at any time from the View menu.

Layouts are also an extension point: installing a layout extension adds new ways to view the same notebook. The `Verso.Showcase.*` packages on NuGet demonstrate this with an image compositor (Image Studio), form-driven notebooks with charts (Form Studio), editable data grids (Grid Studio), and slide-deck authoring with a filmstrip and full-screen presenter (Slide Studio).

## Extension Marketplace

Every built-in feature of Verso is an extension, and you can add more without leaving the editor. The extensions panel includes a marketplace search over your configured NuGet feeds (nuget.org by default): type a package name, pick a version, and install. Because extensions are executable code, Verso asks for consent before loading a downloaded package, and trust is pinned to the exact version you approved.

Notebooks can also declare **required extensions**. When you open such a notebook, Verso resolves and loads them first, prompting once for anything not yet trusted, so a shared notebook brings its own kernels, layouts, and themes with it.

## SQL Database Support

Connect to any ADO.NET-compatible database (SQL Server, PostgreSQL, MySQL, SQLite) and run queries directly in your notebook. Results render as paginated tables with column type tooltips. You can share variables between SQL and C# cells, inspect schema, and scaffold EF Core `DbContext` classes at runtime.

## HTTP Requests

Send REST API requests directly in your notebook using `.http` file syntax, the same format supported by VS Code's REST Client and JetBrains HTTP Client. Responses are formatted with status badges, timing, collapsible headers, and pretty-printed JSON. Declare variables with `@name = value`, use dynamic variables like `{{$guid}}` and `{{$timestamp}}`, chain named requests, and send multiple requests per cell with `###` separators. Response data is automatically shared to C#, F#, and other kernels via the variable store.

## Parameterized Notebooks

A **parameters cell** declares typed inputs with defaults, descriptions, and required flags through a form in the editor, no JSON editing needed. Verso injects the values into the shared variable store before any cell runs, so every kernel sees them as ordinary variables. Fill them in interactively and Run All, or run the same notebook headlessly with the Verso CLI:

```bash
verso run pipeline.verso --param region=us-east --param batchSize=5000
```

String values are coerced to the declared type, and a missing required parameter fails validation instead of running with bad inputs. One notebook can serve as an ad hoc scratchpad, a report template, and a scheduled CI job.

## GitHub Copilot Integration

Verso integrates with GitHub Copilot Chat through the `@verso` participant. With a notebook open, type `@verso` in the Copilot Chat panel to create cells, run code, inspect variables, and explore your notebook using natural language.

**Example prompts:**

- `@verso add a C# cell that generates a list of 100 random numbers`
- `@verso run cell 3 and explain the output`
- `@verso what variables are in scope?`
- `@verso change cell 2 to use LINQ instead of a for loop`

**Slash commands** for common actions without waiting for the LLM:

| Command | Description |
|---------|-------------|
| `@verso /cells` | List all cells with their source code |
| `@verso /run` | Run all cells and show results |
| `@verso /vars` | Show all variables currently in scope |

Copilot can chain multiple actions in a single conversation, for example creating a cell, running it, reading the output, and then fixing an error.

**Agent mode:** Verso also registers twenty language model tools, so in Copilot agent mode Copilot can list, add, edit, move, and run cells, inspect variables, manage parameters, and switch layouts as part of a larger task, without you typing `@verso` at all.

The integration requires GitHub Copilot Chat and VS Code 1.99 or later.

## Comparing Notebooks

Click **Compare** in the notebook toolbar, use the diff icon in the editor title bar, or run **Verso: Compare Notebook with...** from the command palette to see what changed in the open notebook. Compare against the last saved file, git HEAD, any branch, tag, or commit, or another notebook file (`.verso`, `.ipynb`, or `.dib`). The view shows added, removed, edited, and moved cells with side-by-side source comparison, and when a cell's outputs changed, the old and new rendered outputs appear next to each other. Unsaved edits are included by design, and the comparison is read-only. Git baselines use VS Code's built-in git support.

## Importing Existing Notebooks

Already have notebooks in Jupyter or Polyglot format? Open any `.ipynb` or `.dib` file and Verso converts it automatically. SQL connection patterns, language directives, and magic commands are mapped to their native Verso equivalents during import.

## Getting Started

1. Install this extension from the VS Code Marketplace
2. Open an existing `.verso` file or create a new file with a `.verso` extension to start working

Verso runs notebooks on the **.NET runtime, version 8.0 or later** (.NET 8, 9, and 10 are all supported). If a compatible runtime is not already installed, Verso offers to install one for you when you open a notebook, using the .NET Install Tool. To install it yourself instead, download the [.NET runtime](https://dotnet.microsoft.com/download/dotnet) (the .NET SDK works too, but only the runtime is required). To point Verso at a specific installation, set `verso.dotnetPath` in your settings.

> **Note:** Verso also works in browser-based environments such as GitHub Codespaces when accessed from a Chromium-based browser (Chrome or Edge). The first open in a fresh codespace takes longer than on desktop while the notebook runtime downloads; subsequent opens are faster. Safari cannot load the notebook editor in these environments due to a limitation in VS Code's browser webview host, so use Chrome, Edge, or desktop VS Code instead.

### Font Ligatures

Verso respects your VS Code `editor.fontLigatures` setting. For ligatures to render, a ligature-capable font such as [Cascadia Code](https://github.com/microsoft/cascadia-code), [Fira Code](https://github.com/tonsky/FiraCode), or [JetBrains Mono](https://www.jetbrains.com/lp/mono/) must be installed on your system. Verso prepends Cascadia Code and Fira Code to your font stack automatically, so installing either font is all that is needed.

### JavaScript and TypeScript Kernels

The JavaScript kernel works out of the box with no external dependencies by using the built-in Jint interpreter. For full Node.js features (modules, npm packages, async/await, TypeScript), install **Node.js 18 or later**. The kernel auto-detects Node.js on PATH and at well-known install locations (Homebrew, nvm, Volta, fnm). Packages installed with `#!npm` are stored in `~/.verso/node/` and available via `require()` in subsequent cells.

### Python Kernel

The Python kernel requires **Python 3.8-3.12** installed on your system. Python 3.13+ is not yet supported by pythonnet. The kernel auto-detects your Python installation; if auto-detection fails you can set the `PythonDll` option to the path of your Python shared library (e.g. `python312.dll` on Windows, `libpython3.12.dylib` on macOS, `libpython3.12.so` on Linux).

### Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `verso.dotnetPath` | auto-detect | Path to the `dotnet` executable used to run notebooks. If empty, Verso reuses an installed .NET runtime, locating it via the .NET Install Tool. |
| `verso.extensionsPath` | `[]` | Directories of third-party Verso extension assemblies to load on notebook open, one directory per entry. Applies on the next notebook open. |
| `verso.hostPath` | bundled | Path to a custom `Verso.Host.dll`. If empty, the bundled host is used. |
| `verso.preserveOriginalFormat` | `false` | When opening an `.ipynb` file, save changes back to `.ipynb` (cell outputs preserved) instead of converting to a sibling `.verso` file. Leave off to keep the existing convert-on-save behavior. |

## Supported Languages

| Language | IntelliSense | Variable Sharing |
|----------|:------------:|:----------------:|
| C#         | Yes          | Yes              |
| F#         | Yes          | Yes              |
| JavaScript | Yes*         | Yes              |
| TypeScript | Yes*         | Yes              |
| PowerShell | Yes          | Yes              |
| Python     | Yes          | Yes              |
| SQL        | Yes          | Yes              |
| HTTP       | Yes          | Yes              |
| Markdown | N/A          | N/A              |
| HTML     | N/A          | Yes              |
| Mermaid  | N/A          | Yes              |

\* IntelliSense for JavaScript and TypeScript is provided by Monaco's built-in language services rather than the kernel.

## Extensibility

Verso is built on a fully extensible architecture. Every built-in feature, from the C# kernel to the Dark theme, is implemented using the same public interfaces available to extension authors. You can create new language kernels, cell types, themes, layouts, toolbar actions, and data formatters.

See the [extension authoring docs](https://www.versonotebooks.com/docs/index.html) and the [Verso repository](https://github.com/DataficationSDK/Verso) for extension samples and the `dotnet new verso-extension` template.

## Learn More

- [Documentation](https://www.versonotebooks.com/docs/index.html): guides, architecture, and extension authoring
- [Notebook gallery](https://www.versonotebooks.com/gallery/index.html): real notebooks with baked outputs, ready to download and run
- [Release notes](https://www.versonotebooks.com/release-notes.html): what changed in each release

## Support

Found a bug or have a feature request? [Open an issue on GitHub](https://github.com/DataficationSDK/Verso/issues), or ask a question in [Discussions](https://github.com/DataficationSDK/Verso/discussions).

## License

[MIT](https://github.com/DataficationSDK/Verso/blob/main/LICENSE.md)

Verso is a [Datafication](https://datafication.co) project.
