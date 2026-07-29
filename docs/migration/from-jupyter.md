# Migrating from Jupyter

This guide covers migrating Jupyter notebooks (`.ipynb`) to Verso. Whether you are coming from JupyterLab, Jupyter Notebook, or VS Code's built-in Jupyter support, Verso can import your existing notebooks and provide a similar interactive experience with added features like cross-language variable sharing, dashboard layouts, and a full extension model.

## Automatic Import

Verso imports `.ipynb` files directly. No manual conversion is required for standard Jupyter notebooks.

### Opening in VS Code

Open any `.ipynb` file in VS Code with the Verso extension installed. Use **Open With...** and select Verso if the file is associated with another editor. Verso reads the Jupyter format and displays it as a native Verso notebook.

### Opening in the Browser

Launch Verso in the browser with `verso serve`, then open the `.ipynb` file through the UI.

### Converting via the CLI

To convert permanently to the native `.verso` format:

```bash
verso convert notebook.ipynb --to verso
```

The original file is not modified. Use `--output` to specify a different output path, and `--strip-outputs` to remove all cell outputs during conversion:

```bash
verso convert notebook.ipynb --to verso --output cleaned.verso --strip-outputs
```

By default, saving an imported notebook in Verso writes to a sibling `.verso` file and leaves the original untouched. To save back to `.ipynb` with cell outputs preserved, enable the `verso.preserveOriginalFormat` setting in VS Code, or pass `--preserve-format` to `verso repl` / `verso serve`. The flag is off by default to keep existing convert-on-save behavior for users who rely on it.

## What Gets Converted

### Cell Types

| Jupyter Cell Type | Verso Cell Type |
|-------------------|-----------------|
| `code` | Code cell (language set from kernel metadata) |
| `markdown` | Markdown cell |
| `raw` | Imported as an inert, non-executable text cell (no dedicated renderer or editor of its own) |

### Kernel Language

The notebook's programming language is extracted from Jupyter metadata in this order:

1. `metadata.kernelspec.language`
2. `metadata.language_info.name` (fallback)

All code cells are assigned this language. Common mappings:

| Jupyter Kernel | Verso Language |
|----------------|----------------|
| Python (python3, ipykernel) | `python` |
| C# (.NET Interactive) | `csharp` |
| F# (.NET Interactive) | `fsharp` |
| JavaScript (IJavascript) | `javascript` |
| PowerShell | `powershell` |

### Cell Outputs

Jupyter outputs are mapped to Verso's output model:

| Jupyter Output Type | Verso Output |
|--------------------|--------------|
| `stream` (stdout/stderr) | Plain text output |
| `execute_result` | Rich output (HTML preferred, then plain text, then images) |
| `display_data` | Rich output (same MIME priority) |
| `error` | Error output with exception name and traceback |

When a Jupyter output contains multiple MIME types, Verso selects the richest available format in this order: `text/html`, `text/plain`, `image/png`, then the first available type.

The `execution_count` from each cell is preserved in cell metadata.

Exporting the other way, a `stream` output carries the channel it came from: `name` is written as `stderr` for text a cell wrote to standard error and `stdout` otherwise, so a tool reading the exported `.ipynb` sees the same distinction the notebook did.

### Metadata

Notebook-level metadata beyond kernel detection (`kernelspec.name`, `kernelspec.display_name`, `language_info.version`, `language_info.codemirror_mode`) is not carried over. If you need to preserve specific metadata, note it before converting.

## Python Notebooks

Python is the most common Jupyter kernel. When importing a Python notebook:

- All code cells are set to the `python` language
- Python cells run the interpreter installed on your machine, Python 3.8 or newer, picked up from an active virtual environment or conda environment where there is one. See [Python Interpreters](../guides/python-interpreters.md).
- `#!pip` is the Verso form of package installation, though `%pip` and `!` both work as they do in Jupyter:

  **Jupyter:**
  ```python
  !pip install pandas
  %pip install numpy
  ```

  **Verso:**
  ```python
  #!pip pandas
  #!pip numpy
  ```

  You often do not need any of them. Importing a package the environment lacks offers to install it first. See [Python Packages](../guides/python-packages.md).

- Jupyter magic commands (`%matplotlib`, `%%timeit`, `%env`, etc.) are IPython-specific and do not have direct Verso equivalents. These need to be replaced:

  | Jupyter Magic | Verso Alternative |
  |---------------|-------------------|
  | `%matplotlib inline` | Not needed (HTML/SVG outputs display natively) |
  | `%%timeit` / `%time` | `#!time` magic command |
  | `%env VAR value` | Set environment variables in a preceding cell |
  | `%load_ext` | `#!extension` for Verso extensions |
  | `!command` | Supported in Python cells, which run a real interpreter of your own |
  | `%pip install` | Supported, though `#!pip` is the Verso form |

- A `display()` function is built into every Python cell (no import needed) for rich output. When the `IPython` package is installed (via `#!pip IPython`), `IPython.display.display` is wired to the same function, so `from IPython.display import display, HTML, Markdown` works too. Objects that implement `_repr_html_`, `_repr_png_`, or `_repr_svg_` render through it automatically.

## Multi-Language Notebooks (.NET Interactive)

Jupyter notebooks created with .NET Interactive (the engine behind Polyglot Notebooks) receive additional processing. These notebooks typically contain `dotnet_interactive.language` metadata on cells and use `#!` directives to switch languages.

For these notebooks, see the [Polyglot Notebooks migration guide](from-polyglot-notebooks.md), which covers the specific conversion of `#!connect`, `#!share`, `#!set`, and language switching directives.

## Differences from Jupyter

### Multi-Language Support

Jupyter notebooks are typically single-language, determined by the kernel. Verso notebooks support multiple languages in a single notebook. After importing a Python notebook, you can add C#, F#, SQL, JavaScript, and other cell types alongside your existing Python cells.

### Variable Sharing

In Jupyter, all cells share the same kernel and namespace. In Verso, each language has its own kernel, but all kernels share a single variable store. Variables set in a Python cell are available to C#, F#, SQL, and other cells. This is transparent for single-language notebooks but becomes powerful when you add cells in other languages.

Python runs in its own process, so a value from another language reaches it as data. Records arrive as mappings that answer both `row.Field` and `row["Field"]`, dates arrive as real `datetime` values, and a SQL result arrives as a list of rows ready for `pd.DataFrame`. A value with no meaning outside its own process, such as a function or an open connection, is still bound to its name but explains itself when printed or used. See [Language Kernels](../guides/language-kernels.md) for the details.

### Package Management

| Jupyter | Verso |
|---------|-------|
| `!pip install package` or `%pip install package` | `#!pip package` |
| `!npm install package` | `#!npm package` |
| N/A | `#r "nuget: Package"` (for .NET packages) |

### Rich Output

Jupyter uses IPython's display system (`display()`, `_repr_html_()`, `_repr_png_()`, etc.). Verso uses its own output model based on MIME types. Kernels can produce `text/plain`, `text/html`, `image/png`, `image/svg+xml`, `application/json`, and other MIME types. Data formatters convert runtime objects to displayable outputs.

### Notebook Format

Jupyter uses `.ipynb` (JSON with outputs and metadata). Verso uses `.verso` (also JSON, but with layout support, parameter definitions, theme preferences, and extension settings). The `.verso` format is designed to be diff-friendly with consistent field ordering and indentation.

### Layouts

Jupyter has a fixed linear cell layout. Verso adds a dashboard layout where cells can be arranged in a 12-column grid with drag-and-drop positioning. Imported notebooks start in the standard linear layout, and you can switch to the dashboard layout at any time.

### Extensions

Jupyter uses a kernel/server extension model. Verso's extension model is based on .NET interfaces, where a single extension can provide kernels, renderers, themes, formatters, toolbar actions, and more. Extensions are loaded via `#!extension` or co-deployed with the engine.

## Limitations

- **Round-trip to `.ipynb` is opt-in and lossy for non-standard cell types.** With `verso.preserveOriginalFormat` (or `--preserve-format`) on, `.ipynb` files save back as `.ipynb` with code, markdown, and cell outputs preserved. Verso-specific cell types (HTML, Mermaid, SQL, HTTP) are written as Jupyter `raw` cells with the original type stashed in `cell.metadata.verso_type` (not read back on import, so reopening the file restores them as inert `raw` cells); notebook-level features like layouts, parameter definitions, and theme preferences are not represented in the Jupyter format.
- **Jupyter notebook format versions below 4 are not supported.** Notebooks created with very old versions of Jupyter (nbformat 1-3) need to be upgraded to nbformat 4 first (open and re-save in JupyterLab).
- **IPython magics are not converted.** Cell and line magics (`%`, `%%`) are Python kernel-specific and pass through as literal text.
- **Widget outputs stored in an imported `.ipynb` do not render.** A widget output saved by Jupyter is a reference to model state held in that file's own metadata, which the importer does not read, so it arrives as whatever text accompanied it. Re-running the cell in Verso draws the widget: libraries built on `ipywidgets`, among them k3d, ipyleaflet, pythreejs, bqplot, and ipyvolume, are rendered in Python cells. Interaction with them is one-way, so a slider wired to a Python variable does not run the Python behind it. See [Language Kernels](../guides/language-kernels.md).
