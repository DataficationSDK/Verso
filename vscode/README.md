# Verso Notebook

**Interactive .NET notebooks for VS Code with C#, F#, PowerShell, Python, SQL, and more.**

![Verso in action](https://datafication.co/assets/verso/RunningVersoNotebook.gif)

## Features

- **C#, F#, PowerShell, and Python with IntelliSense** including completions, diagnostics, hover info, and NuGet package references
- **SQL database connectivity** with paginated results, schema inspection, and EF Core scaffolding
- **Markdown, HTML, and Mermaid** cells for documentation, visualizations, and diagrams
- **Variable sharing** across languages so you can set a value in C# and use it in SQL, F#, or PowerShell
- **Notebook and Dashboard layouts** to view cells as a linear document or arrange them in a drag-and-drop grid
- **Light, Dark, and High Contrast themes** that are hot-swappable at runtime (High Contrast meets WCAG 2.1 AA)
- **Import Jupyter (.ipynb) and Polyglot (.dib) notebooks** with automatic conversion of magic commands and cell types
- **Fully extensible** with a public API for adding new languages, cell types, themes, layouts, and more

## Writing Code with IntelliSense

Verso's C# kernel is powered by Roslyn, giving you the latest language features, persistent state across cells, real-time error checking, and code completions as you type. The F# kernel offers the same experience powered by FSharp.Compiler.Service. The PowerShell kernel hosts a persistent runspace with full cmdlet support, pipeline-aware output, and completions powered by `CommandCompletion`. The Python kernel embeds CPython via pythonnet with IntelliSense powered by jedi, bidirectional variable sharing with other kernels, and virtual environment support via `#!pip`.

![C# IntelliSense in Verso](https://datafication.co/assets/verso/IntellisenseVerso.gif)

## Notebook and Dashboard Layouts

Every notebook can be viewed in two ways. **Notebook layout** presents cells in a familiar top-to-bottom flow. **Dashboard layout** lets you drag and resize cells on a 12-column grid to build interactive dashboards. Both layouts are saved in the `.verso` file and you can switch between them at any time.

## SQL Database Support

Connect to any ADO.NET-compatible database (SQL Server, PostgreSQL, MySQL, SQLite) and run queries directly in your notebook. Results render as paginated tables with column type tooltips. You can share variables between SQL and C# cells, inspect schema, and scaffold EF Core `DbContext` classes at runtime.

## Importing Existing Notebooks

Already have notebooks in Jupyter or Polyglot format? Open any `.ipynb` or `.dib` file and Verso converts it automatically. SQL connection patterns, language directives, and magic commands are mapped to their native Verso equivalents during import.

## Getting Started

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Install this extension from the VS Code Marketplace
3. Open an existing `.verso` file or create a new file with a `.verso` extension to start working

### Python Kernel

The Python kernel requires **Python 3.8–3.12** installed on your system. Python 3.13+ is not yet supported by pythonnet. The kernel auto-detects your Python installation; if auto-detection fails you can set the `PythonDll` option to the path of your Python shared library (e.g. `python312.dll` on Windows, `libpython3.12.dylib` on macOS, `libpython3.12.so` on Linux).

To import an existing notebook, use **File > Open** on any `.ipynb` or `.dib` file.

## Supported Languages

| Language | IntelliSense | Variable Sharing |
|----------|:------------:|:----------------:|
| C#         | Yes          | Yes              |
| F#         | Yes          | Yes              |
| PowerShell | Yes          | Yes              |
| Python     | Yes          | Yes              |
| SQL        | Yes          | Yes              |
| Markdown | N/A          | N/A              |
| HTML     | N/A          | Yes              |
| Mermaid  | N/A          | Yes              |

## Extensibility

Verso is built on a fully extensible architecture. Every built-in feature, from the C# kernel to the Dark theme, is implemented using the same public interfaces available to extension authors. You can create new language kernels, cell types, themes, layouts, toolbar actions, and data formatters.

See the [Verso repository](https://github.com/DataficationSDK/Verso) for documentation, extension samples, and the `dotnet new verso-extension` template.

## Support

Found a bug or have a feature request? [Open an issue on GitHub](https://github.com/DataficationSDK/Verso/issues).

## License

[MIT](https://github.com/DataficationSDK/Verso/blob/main/LICENSE.md)

Verso is a [Datafication](https://datafication.co) project.
