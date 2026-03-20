# Verso

**Open-source interactive notebook platform and embeddable .NET execution engine.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.md)
[![.NET 8 | 10](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
![CI](https://img.shields.io/github/actions/workflow/status/DataficationSDK/Verso/verso-ci.yml?branch=main&label=CI)
[![NuGet](https://img.shields.io/nuget/v/Verso?label=NuGet)](https://www.nuget.org/packages/Verso)
[![GitHub Release](https://img.shields.io/github/v/release/DataficationSDK/Verso?label=Release)](https://github.com/DataficationSDK/Verso/releases)
[![VS Code Marketplace](https://img.shields.io/visual-studio-marketplace/v/Datafication.verso-notebook?label=VS%20Code%20Marketplace)](https://marketplace.visualstudio.com/items?itemName=Datafication.verso-notebook)

![Verso in action](https://datafication.co/assets/verso/RunningVersoNotebook.gif)

## Why Verso

Microsoft deprecated Polyglot Notebooks on February 11, 2026, and .NET Interactive, the engine that powered it, followed the same path. Together they were the primary way to run interactive C#, F#, PowerShell, and SQL in a notebook. Their deprecation left a gap in the .NET ecosystem: no maintained notebook platform, and no maintained embeddable execution engine.

Verso fills both roles.

**As a notebook platform**, Verso runs in VS Code or any browser, ships with IntelliSense and variable sharing across nine languages, and imports existing `.ipynb` and `.dib` files. If you used Polyglot Notebooks, the experience will feel familiar.

**As an engine**, the core is a headless .NET library with no UI dependencies. It provides multi-language execution, an extension host, a variable store, and a layout manager through a clean set of public interfaces. If you embedded .NET Interactive in a tool, service, or workflow, the Verso engine serves the same purpose with a fully extensible architecture. Reference the NuGet package, wire up a `Scaffold`, and you have a programmable notebook runtime in any .NET application.

The architecture is built on one principle: every feature is an extension, and every extension uses the same public interfaces available to anyone. The C# kernel, the dark theme, and the dashboard layout all ship as extensions with no special access to engine internals. If a built-in feature needs an internal API to work, the interfaces are incomplete.

## Languages

| Language | IntelliSense | Variable Sharing |
|----------|:------------:|:----------------:|
| C#         | Yes | Yes |
| F#         | Yes | Yes |
| PowerShell | Yes | Yes |
| Python     | Yes | Yes |
| SQL        | Yes | Yes |
| HTTP       | Yes | Yes |
| Markdown   | N/A | N/A |
| HTML       | N/A | Yes |
| Mermaid    | N/A | Yes |

## Architecture

Verso is split into three layers. The engine knows nothing about the UI. The UI knows nothing about the host environment. Extensions work identically everywhere.

```
┌─────────────────────────────────────────────────────────┐
│  Front-Ends                                             │
│  ┌─────────────────────┐  ┌──────────────────────────┐  │
│  │  VS Code Extension  │  │  Blazor Server Web App   │  │
│  │  (Blazor WASM       │  │  (runs in any browser)   │  │
│  │   inside a webview) │  │                          │  │
│  └──────────┬──────────┘  └────────────┬─────────────┘  │
│             │                          │                │
│  ┌──────────────────────────────────────────────────┐   │
│  │  Shared UI (Razor Class Library)                 │   │
│  │  Monaco editor, panels, toolbar, theme provider  │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
                          │
┌─────────────────────────────────────────────────────────┐
│  Verso Engine (headless .NET library, no UI)            │
│  ┌────────────────────────────────────────────────────┐ │
│  │  Scaffold · Extension Host · Execution Pipeline    │ │
│  │  Layout Manager · Theme Engine · Variable Store    │ │
│  ├────────────────────────────────────────────────────┤ │
│  │  Built-in Extensions                               │ │
│  │  C# Kernel · Markdown · HTML · Mermaid · Themes    │ │
│  │  Notebook Layout · Dashboard Layout · Formatters   │ │
│  ├────────────────────────────────────────────────────┤ │
│  │  First-Party Extension Packages                    │ │
│  │  Verso.FSharp · Verso.PowerShell · Verso.Python    │ │
│  │  Verso.Ado (SQL) · Verso.Http (HTTP)               │ │
│  └────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
                          │
┌─────────────────────────────────────────────────────────┐
│  Verso.Abstractions                                     │
│  Pure interfaces, zero dependencies                     │
│  The only package extension authors need to reference   │
└─────────────────────────────────────────────────────────┘
```

**Blazor Server** talks to the engine directly, in-process. **VS Code** runs Blazor WebAssembly in a webview, which communicates with a host process over JSON-RPC. Both use the same shared Razor components, so the experience is identical in both environments.

## Features

### Code Execution with IntelliSense

![C# code execution with IntelliSense](https://datafication.co/assets/verso/IntellisenseVerso.gif)

All language kernels provide completions, diagnostics, and hover information. NuGet packages are referenced inline with `#r "nuget: PackageName/Version"`, and custom package sources are supported with `#i "nuget: <url>"`. Python uses `#!pip` for package management. State persists across cells within each kernel, and variables are shared across kernels through a central variable store.

### Layouts

The same notebook can be viewed as a linear document or rearranged into a 12-column grid dashboard. Drag cells to reposition them, resize with handles, and the layout metadata is saved in the `.verso` file. Switch between layouts at runtime.

![Side-by-side comparison of Notebook Layout and Dashboard Layout](images/notebook-dashboard-side-by-side.png)

### Database Connectivity

Verso.Ado provides provider-agnostic SQL connectivity through ADO.NET. Connect to any supported database, execute queries with paginated result tables, inspect schema, and scaffold EF Core DbContext classes at runtime.

### HTTP Requests

Verso.Http uses `.http` file syntax (the same format supported by VS Code REST Client and JetBrains HTTP Client). Features include variable interpolation, dynamic variables, named request chaining, and cross-kernel integration where response data is shared to C#, F#, and other cells.

### Rich Content Cells

Markdown (rendered via Markdig), raw HTML, and Mermaid diagram cells all support `@variable` substitution from the shared variable store, enabling dynamic documents that update when data changes.

### Themes

Three built-in themes (Light, Dark, High Contrast) are hot-swappable at runtime. The High Contrast theme meets WCAG 2.1 AA contrast requirements. In VS Code, the notebook theme automatically follows your editor theme.

### Import from Jupyter and Polyglot Notebooks

Open any `.ipynb` or `.dib` file and Verso converts it automatically. Polyglot Notebook patterns like `#!fsharp`, `#!connect`, and `#!sql` are mapped to native Verso cells during import. Saving writes to a `.verso` file, preserving the original.

## Extension Model

Eleven interfaces define every point of extensibility:

| Interface | Purpose |
|-----------|---------|
| `ILanguageKernel` | Execute code, provide completions, diagnostics, and hover for a language |
| `ICellRenderer` | Render the input and output areas of a cell |
| `ICellType` | Pair a renderer with an optional kernel to define a new cell type |
| `IToolbarAction` | Add buttons to the notebook toolbar or cell menus |
| `IDataFormatter` | Format runtime objects into displayable outputs |
| `IMagicCommand` | Define directives like `#!time` that extend kernel behavior |
| `ITheme` | Provide colors, typography, spacing, and syntax highlighting |
| `ILayoutEngine` | Manage spatial arrangement of cells |
| `INotebookSerializer` | Read and write notebook file formats |
| `INotebookPostProcessor` | Transform notebooks after load or before save |
| `ICellInteractionHandler` | Handle bidirectional interactions from rendered cell content back to extension code |

Extensions can also implement `IExtensionSettings` to expose configurable settings in the UI.

Third-party extensions load in their own `AssemblyLoadContext`, collectible and unloadable. Your extension references only `Verso.Abstractions` and works across every front-end without modification.

```bash
dotnet new verso-extension -n MyExtension
```

Verso includes a `dotnet new` template, a testing library (`Verso.Testing`), and sample extensions in the repo:

| Sample | What It Shows |
|--------|--------------|
| **Dice** | Custom kernel + renderer + toolbar action |
| **Presentation Layout** | Slide-based navigation layout |
| **Diagram Editor** | Custom cell type with its own kernel |

## What Ships Out of the Box

| Category | Included |
|----------|----------|
| **Kernels** | C# (Roslyn), F# (FCS), PowerShell, Python (pythonnet), HTTP |
| **Cell Types** | Code, Markdown, HTML, Mermaid, SQL, HTTP |
| **Themes** | Light, Dark, High Contrast (WCAG 2.1 AA) |
| **Layouts** | Notebook (linear), Dashboard (12-column CSS grid) |
| **Magic Commands** | `#!time`, `#!nuget`, `#!pip`, `#!extension`, `#!restart`, `#!about`, `#!import`, `#!http-set-base`, `#!http-set-header`, `#!http-set-timeout` |
| **Toolbar Actions** | Run Cell, Run All, Clear Outputs, Restart, Switch Layout, Switch Theme, Export HTML, Export Markdown |
| **Data Formatters** | Primitives, Collections (HTML tables), HTML, Images, SVG, Exceptions, F# types, SQL result sets |
| **Serializers** | `.verso` (native JSON), `.ipynb` import, `.dib` import |

## The `.verso` File Format

JSON-based, human-readable, and diff-friendly:

```json
{
  "verso": "1.0",
  "metadata": {
    "defaultKernel": "csharp",
    "activeLayout": "notebook",
    "preferredTheme": "verso-light"
  },
  "cells": [
    {
      "id": "...",
      "type": "code",
      "language": "csharp",
      "source": "Console.WriteLine(\"Hello from Verso\");",
      "outputs": [...]
    }
  ],
  "layouts": {
    "dashboard": {
      "cells": {
        "cell-id": { "row": 0, "col": 0, "width": 6, "height": 4 }
      }
    }
  }
}
```

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [VS Code](https://code.visualstudio.com/) (for the extension, desktop only) or any modern browser (for Blazor)
- [Python 3.8-3.12](https://www.python.org/downloads/) (optional, for the Python kernel)

### Run in the Browser

```bash
git clone https://github.com/DataficationSDK/Verso
cd Verso
dotnet build Verso.sln
dotnet run --project src/Verso.Blazor
```

### Run in VS Code

```bash
dotnet build src/Verso.Host
cd vscode
npm install
npm run build:all
npx vsce package --skip-license
```

Install the `.vsix` file, then open any `.verso` file. Use **Open With...** to import `.ipynb` or `.dib` files.

### Run the Tests

```bash
dotnet test Verso.sln
```

## Contributing

Contributions are welcome. Open an issue to discuss what you'd like to work on. A formal `CONTRIBUTING.md` guide is coming as part of an upcoming milestone.

## License

[MIT](LICENSE.md)

Verso is a Datafication project.
