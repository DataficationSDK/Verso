# Verso

Core engine for the [Verso](https://github.com/DataficationSDK/Verso) extensible notebook platform.

## Overview

Verso is a headless notebook engine with no UI dependencies. It provides C# scripting powered by Roslyn, an extension host, theme engine, layout manager, execution pipeline, and all built-in extensions. Front-ends consume it through a VS Code extension or a standalone Blazor application.

### Built-in Features

- **C# kernel** with IntelliSense, diagnostics, hover info, and NuGet references
- **Markdown rendering** via Markdig
- **Data formatters** for primitives, collections, HTML, images, SVG, and exceptions
- **Three themes** — Light, Dark, and High Contrast (WCAG 2.1 AA)
- **Two layouts** — Notebook (linear) and Dashboard (12-column grid)
- **Magic commands** — `#!time`, `#!nuget`, `#!restart`, `#!about`, `#!import`
- **Serializers** — `.verso` native format and `.ipynb` import

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
| [Verso.Testing](https://www.nuget.org/packages/Verso.Testing) | Test utilities for extensions |
| [Verso.Templates](https://www.nuget.org/packages/Verso.Templates) | `dotnet new` project templates |
