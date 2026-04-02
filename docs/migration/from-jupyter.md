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

Saving an imported notebook in Verso always writes to a new `.verso` file, preserving the original.

## What Gets Converted

### Cell Types

| Jupyter Cell Type | Verso Cell Type |
|-------------------|-----------------|
| `code` | Code cell (language set from kernel metadata) |
| `markdown` | Markdown cell |
| `raw` | Raw cell |

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

### Metadata

Notebook-level metadata beyond kernel detection (`kernelspec.name`, `kernelspec.display_name`, `language_info.version`, `language_info.codemirror_mode`) is not carried over. If you need to preserve specific metadata, note it before converting.

## Python Notebooks

Python is the most common Jupyter kernel. When importing a Python notebook:

- All code cells are set to the `python` language
- Python package management uses `#!pip` instead of `!pip` or `%pip`:

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

- Jupyter magic commands (`%matplotlib`, `%%timeit`, `%env`, etc.) are IPython-specific and do not have direct Verso equivalents. These need to be replaced:

  | Jupyter Magic | Verso Alternative |
  |---------------|-------------------|
  | `%matplotlib inline` | Not needed (HTML/SVG outputs display natively) |
  | `%%timeit` / `%time` | `#!time` magic command |
  | `%env VAR value` | Set environment variables in a preceding cell |
  | `%load_ext` | `#!extension` for Verso extensions |
  | `!command` | Shell commands depend on the kernel (not directly supported in Python cells) |

- Jupyter's `display()` and `IPython.display` APIs are not available. Use `print()` for text output. Rich HTML output depends on the Python kernel's formatting capabilities.

## Multi-Language Notebooks (.NET Interactive)

Jupyter notebooks created with .NET Interactive (the engine behind Polyglot Notebooks) receive additional processing. These notebooks typically contain `dotnet_interactive.language` metadata on cells and use `#!` directives to switch languages.

For these notebooks, see the [Polyglot Notebooks migration guide](from-polyglot-notebooks.md), which covers the specific conversion of `#!connect`, `#!share`, `#!set`, and language switching directives.

## Differences from Jupyter

### Multi-Language Support

Jupyter notebooks are typically single-language, determined by the kernel. Verso notebooks support multiple languages in a single notebook. After importing a Python notebook, you can add C#, F#, SQL, JavaScript, and other cell types alongside your existing Python cells.

### Variable Sharing

In Jupyter, all cells share the same kernel and namespace. In Verso, each language has its own kernel, but all kernels share a single variable store. Variables set in a Python cell are available to C#, F#, SQL, and other cells. This is transparent for single-language notebooks but becomes powerful when you add cells in other languages.

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

- **Export back to `.ipynb` is not supported.** Conversion is one-way. If you need to maintain a Jupyter-compatible version, keep the original `.ipynb` file.
- **Jupyter notebook format versions below 4 are not supported.** Notebooks created with very old versions of Jupyter (nbformat 1-3) need to be upgraded to nbformat 4 first (open and re-save in JupyterLab).
- **IPython magics are not converted.** Cell and line magics (`%`, `%%`) are Python kernel-specific and pass through as literal text.
- **Jupyter widgets are not supported.** Interactive widgets (`ipywidgets`) do not have a Verso equivalent. Static output from widgets (HTML snapshots) may be preserved in cell outputs.
- **Kernel-specific display functions** (`display()`, `HTML()`, `Markdown()`) from `IPython.display` are not available in Verso's Python kernel.
