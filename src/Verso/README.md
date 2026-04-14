# Verso

Core engine for the [Verso](https://github.com/DataficationSDK/Verso) extensible notebook platform.

## Overview

Verso is a headless notebook engine with no UI dependencies. It provides C# scripting powered by Roslyn, an extension host, theme engine, layout manager, execution pipeline, and all built-in extensions. Front-ends consume it through a VS Code extension or a standalone Blazor application.

### Built-in Features

- **C# kernel** with IntelliSense, diagnostics, hover info, and NuGet references
- **Markdown rendering** via Markdig
- **HTML and Mermaid cell types** with variable substitution from the shared store
- **Data formatters** for primitives, collections, objects, HTML, images, SVG, and exceptions
- **Three themes** (Light, Dark, and High Contrast meeting WCAG 2.1 AA)
- **Three layouts** (Notebook linear, Dashboard 12-column grid, Presentation slide-based)
- **Magic commands** (`#!time`, `#!nuget`, `#!extension`, `#!restart`, `#!about`, `#!import`)
- **Serializers** (`.verso` native format, `.ipynb` Jupyter import, `.dib` Polyglot Notebooks import)
- **Toolbar actions** (Run Cell, Run All, Clear Outputs, Restart Kernel, Switch Layout, Switch Theme, Export HTML, Export Markdown)

## Installation

```shell
dotnet add package Verso
```

This package depends on [Verso.Abstractions](https://www.nuget.org/packages/Verso.Abstractions).

## Related Packages

| Package | Description |
|---------|-------------|
| [Verso.Abstractions](https://www.nuget.org/packages/Verso.Abstractions) | Extension interfaces (for extension authors) |
| [Verso.Ado](https://www.nuget.org/packages/Verso.Ado) | SQL database connectivity |
| [Verso.FSharp](https://www.nuget.org/packages/Verso.FSharp) | F# Interactive kernel |
| [Verso.PowerShell](https://www.nuget.org/packages/Verso.PowerShell) | PowerShell kernel |
| [Verso.Python](https://www.nuget.org/packages/Verso.Python) | Python kernel via pythonnet |
| [Verso.Http](https://www.nuget.org/packages/Verso.Http) | HTTP request cell type |
| [Verso.JavaScript](https://www.nuget.org/packages/Verso.JavaScript) | JavaScript kernel |
| [Verso.Cli](https://www.nuget.org/packages/Verso.Cli) | Command-line interface |
| [Verso.Testing](https://www.nuget.org/packages/Verso.Testing) | Test utilities for extensions |
| [Verso.Templates](https://www.nuget.org/packages/Verso.Templates) | `dotnet new` project templates |
