# Architecture Overview

Verso is built on a strict layered architecture with one guiding principle: every feature is an extension, and every extension uses the same public interfaces available to anyone. The C# kernel, the dark theme, and the dashboard layout all ship as extensions with no special access to engine internals.

The architecture separates into three layers. The engine knows nothing about the UI. The UI knows nothing about the host environment. Extensions work identically everywhere.

```
+-----------------------------------------------------------+
|  Front-Ends                                               |
|  +---------------------+  +--------------------------+    |
|  |  VS Code Extension  |  |  Blazor Server Web App   |    |
|  |  (Blazor WASM       |  |  (verso serve, or        |    |
|  |   inside a webview) |  |  dotnet run Verso.Blazor)|    |
|  +----------+----------+  +-------------+------------+    |
|             |                           |                 |
|  +----------------------------------------------------+   |
|  |  Shared UI (Razor Class Library)                   |   |
|  |  Monaco editor, panels, toolbar, theme provider    |   |
|  +----------------------------------------------------+   |
|                                                           |
|  +----------------------------------------------------+   |
|  |  CLI (verso run / verso convert)                   |   |
|  |  Headless execution, format conversion, CI/CD      |   |
|  +----------------------------------------------------+   |
+-----------------------------------------------------------+
                           |
+-----------------------------------------------------------+
|  Verso Engine (headless .NET library, no UI)              |
|  +------------------------------------------------------+ |
|  |  Scaffold - Extension Host - Execution Pipeline      | |
|  |  Layout Manager - Theme Engine - Variable Store      | |
|  +------------------------------------------------------+ |
|  |  Built-in Extensions                                 | |
|  |  C# Kernel - Markdown - HTML - Mermaid - Themes      | |
|  |  Notebook Layout - Dashboard Layout - Formatters     | |
|  +------------------------------------------------------+ |
|  |  First-Party Extension Packages                      | |
|  |  Verso.FSharp - Verso.JavaScript - Verso.PowerShell  | |
|  |  Verso.Python - Verso.Ado (SQL) - Verso.Http         | |
|  +------------------------------------------------------+ |
+-----------------------------------------------------------+
                           |
+-----------------------------------------------------------+
|  Verso.Abstractions                                       |
|  Pure interfaces, zero dependencies                       |
|  The only package extension authors need to reference     |
+-----------------------------------------------------------+
```

## The Three Layers

### Verso.Abstractions

The foundation of the entire system. This package contains only interfaces, records, enums, and the `[VersoExtension]` attribute. It has zero dependencies beyond the .NET BCL.

Extension authors reference this package and nothing else. All twelve extension interfaces inherit from `IExtension`, which provides identity (`ExtensionId`, `Name`, `Version`), optional metadata (`Author`, `Description`), and lifecycle hooks (`OnLoadedAsync`, `OnUnloadedAsync`).

The interfaces are:

| Interface | Purpose |
|-----------|---------|
| `ILanguageKernel` | Execute code, provide completions, diagnostics, and hover |
| `ICellRenderer` | Render input and output areas of a cell |
| `ICellType` | Pair a renderer with an optional kernel to define a new cell type |
| `IToolbarAction` | Add buttons to the notebook or cell toolbar |
| `IDataFormatter` | Format runtime objects into displayable outputs |
| `IMagicCommand` | Define `#!` directives that extend kernel behavior |
| `ITheme` | Provide colors, typography, spacing, and syntax highlighting |
| `ILayoutEngine` | Manage spatial arrangement of cells |
| `INotebookSerializer` | Read and write notebook file formats |
| `INotebookPostProcessor` | Transform notebooks after load or before save |
| `ICellInteractionHandler` | Handle interactions from rendered cell content back to extension code |
| `ICellPropertyProvider` | Contribute configurable property sections to the cell properties panel |

Extensions can also implement `IExtensionSettings` to expose configurable settings in the UI.

Context interfaces (`IVersoContext`, `IExecutionContext`, `ICellRenderContext`, `IMagicCommandContext`) provide extensions with access to shared services without coupling them to engine internals. See the [extension-host](extension-host.md) document for details on how extensions are discovered and loaded.

### Verso Engine

The engine is a headless .NET library with no UI dependencies. Its central class is `Scaffold`, which orchestrates a notebook session by owning:

- **Cell management** for the in-memory `NotebookModel`
- **Kernel registry** for resolving which language kernel handles a cell
- **Execution dispatch** through `ExecutionPipeline` for both kernel-backed and renderer-only cells
- **Variable Store** for cross-kernel state sharing
- **Extension Host** for discovering, loading, validating, and managing extensions
- **Subsystems** including `ThemeEngine`, `LayoutManager`, and `SettingsManager`

The engine is designed for embedding. Any .NET application can reference the Verso NuGet package, create a `Scaffold`, load extensions, and execute notebook cells programmatically. The front-ends are consumers of this API, not part of it.

See the [engine](engine.md) and [execution-pipeline](execution-pipeline.md) documents for detailed coverage.

### Front-Ends

Three front-ends consume the engine:

**Blazor Server** talks to the engine directly, in-process. A single `ServerNotebookService` wraps a `Scaffold` and `ExtensionHost` instance, exposing notebook operations to the Razor components through the `INotebookService` interface. This is the simplest hosting model and the one used by `verso serve`.

**VS Code** runs Blazor WebAssembly in a webview. The WASM app has no reference to the engine. Instead, a `RemoteNotebookService` forwards all operations through a JavaScript bridge to the VS Code extension, which relays them over JSON-RPC (stdin/stdout) to a `Verso.Host` process. The host process holds the `Scaffold` and runs the engine. This separation means the UI runs in the browser's sandbox while the engine has full .NET runtime access.

**The CLI** (`verso run`, `verso convert`) drives the engine headlessly with no UI. A `HeadlessRunner` creates a `Scaffold`, loads extensions, resolves parameters, and executes cells sequentially. Output is rendered to the terminal or serialized to JSON. This is the entry point for CI/CD pipelines.

All three visual front-ends (Blazor Server, VS Code, and `verso serve`) share the same Razor components from the `Verso.Blazor.Shared` class library, so the notebook experience is identical regardless of hosting environment.

See the [front-ends](front-ends.md) document for details on each hosting model and the communication patterns between them.

## Key Design Decisions

### Everything Is an Extension

The engine provides no built-in language support, themes, layouts, or formatters. All of these ship as extensions that are discovered and loaded at startup. Built-in extensions are co-deployed alongside the engine assembly and loaded into the default `AssemblyLoadContext`. Third-party extensions are loaded into isolated, collectible `AssemblyLoadContext` instances for safe unloading.

This means the engine can be shipped with a custom set of extensions, or with none at all. It also means that if a built-in feature needs an internal API to work, the interfaces are incomplete.

### Single Variable Store

All kernels in a session share one `VariableStore` instance. There is no per-kernel isolation. A C# cell can set a variable that an F# cell reads, and vice versa. This flat, shared model keeps cross-language interop simple and predictable.

### Fresh Pipeline Per Execution

Each call to `ExecuteCellAsync` creates a new `ExecutionPipeline` instance. The pipeline is not reused across cells. This avoids state leaks between executions and keeps the pipeline stateless.

### UI/Engine Separation

The engine has no knowledge of Blazor, VS Code, or any rendering technology. The `INotebookService` interface in `Verso.Blazor.Shared` is the boundary between UI and engine. Each hosting environment provides its own implementation: `ServerNotebookService` (in-process), `RemoteNotebookService` (JSON-RPC), or `HeadlessRunner` (no UI).

## Project Dependency Graph

```
Verso.Abstractions          (interfaces only, zero deps)
    ^
    |
Verso                       (engine: Scaffold, ExtensionHost, Pipeline)
    ^
    |--- Verso.FSharp       (F# kernel extension)
    |--- Verso.JavaScript   (JS/TS kernel extension)
    |--- Verso.PowerShell   (PowerShell kernel extension)
    |--- Verso.Python       (Python kernel extension)
    |--- Verso.Ado          (SQL kernel extension)
    |--- Verso.Http         (HTTP kernel extension)
    ^
    |
Verso.Blazor.Shared         (Razor Class Library, shared components)
    ^
    |--- Verso.Blazor       (Blazor Server host)
    |--- Verso.Blazor.Wasm  (Blazor WASM for VS Code webview)
    ^
    |
Verso.Host                  (JSON-RPC host for VS Code)
Verso.Cli                   (CLI tool: serve, run, convert, info)
```

## Further Reading

- [Engine](engine.md) -- Scaffold, Variable Store, subsystems
- [Front-Ends](front-ends.md) -- Blazor Server, VS Code, CLI, shared UI
- [Extension Host](extension-host.md) -- Discovery, loading, isolation, lifecycle
- [Execution Pipeline](execution-pipeline.md) -- Cell execution flow, kernel dispatch, magic commands
