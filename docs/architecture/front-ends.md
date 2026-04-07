# Front-Ends

Verso has three front-ends that consume the engine. Two are visual (Blazor Server and VS Code) and share the same Razor component library. The third is the CLI, which drives the engine headlessly. All three use the same engine API, so the notebook behavior is identical regardless of hosting environment.

## Shared UI (Verso.Blazor.Shared)

The `Verso.Blazor.Shared` project is a Razor Class Library that contains every UI component used by both the Blazor Server and VS Code front-ends. Neither host implements its own notebook rendering. They inject an `INotebookService` and the shared components handle everything.

### Key Components

| Component | Description |
|-----------|-------------|
| `Cell.razor` | Full cell widget: gutter (run button, cell index, collapse chevron), toolbar (cell type, language, actions), Monaco editor, and output area. Handles all MIME types inline including HTML, SVG, Mermaid, images, JSON trees, and CSV tables. |
| `Toolbar.razor` | Top-level notebook toolbar: file operations (hidden in VS Code), run all, layout switcher, theme switcher, export menu, and extension-defined actions. |
| `MonacoEditor.razor` | Monaco editor wrapper with parameters for value, language, and callbacks for completions, hover, and diagnostics. |
| `DashboardGrid.razor` | Grid layout container for dashboard mode. |
| `ExtensionPanel.razor` | Sidebar listing loaded extensions with enable/disable controls. |
| `CellPropertiesPanel.razor` | Cell properties sidebar showing extension-contributed property sections for the selected cell. Conditional on the active layout's `SupportsPropertiesPanel` flag. |
| `VariableExplorer.razor` | Variable inspector sidebar showing all shared variables. |
| `ThemeProvider.razor` | Injects CSS variables from the active theme into a `<style>` tag. |
| `SettingsPanel.razor` | Extension settings editor. |
| `ExtensionConsentDialog.razor` | Modal dialog for approving third-party extension packages. |

### INotebookService

`INotebookService` (namespace `Verso.Blazor.Shared.Services`) is the abstraction that decouples the UI from the engine. It covers notebook CRUD, cell operations, execution, IntelliSense, layout/theme switching, extension management, variable exploration, toolbar actions, and cell interaction dispatch.

Each hosting environment provides its own implementation:

| Host | Implementation | Engine Access |
|------|----------------|---------------|
| Blazor Server | `ServerNotebookService` | Direct, in-process |
| VS Code (WASM) | `RemoteNotebookService` | JSON-RPC via host process |

### Static Assets

The shared project includes JavaScript interop files in `wwwroot/js/`:

| File | Purpose |
|------|---------|
| `monaco-interop.js` | Monaco editor initialization, completions, hover, diagnostics |
| `cell-interact-interop.js` | Click/event dispatch for interactive cell outputs |
| `cell-drag-interop.js` | Cell reordering via drag-and-drop |
| `dashboard-interop.js` | Dashboard grid drag and resize |
| `parameters-interop.js` | Parameter cell form event delegation |
| `mermaid-interop.js` | Mermaid diagram rendering |
| `panel-resize-interop.js` | Sidebar resize handle |
| `file-download-interop.js` | Browser download trigger (Server mode) |
| `user-prefs-interop.js` | VS Code global state read/write |
| `tag-input-interop.js` | Tag input field (comma/Enter to add tags) for the properties panel |

## Blazor Server (Verso.Blazor)

The simplest hosting model. The engine runs in-process alongside the Blazor Server app. There is no serialization boundary between the UI and the engine.

### Startup

`Program.cs` configures a standard Blazor Server application:

```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => {
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromHours(1);
    });
builder.Services.AddScoped<INotebookService, ServerNotebookService>();
```

The `DisconnectedCircuitRetentionPeriod` is set to one hour so users can recover from brief network interruptions without losing their session.

### ServerNotebookService

`ServerNotebookService` (namespace `Verso.Blazor.Services`) directly holds a `Scaffold` and `ExtensionHost`. When a notebook is opened or created:

1. Creates a fresh `ExtensionHost` and calls `LoadBuiltInExtensionsAsync()`
2. Wires the consent handler for the extension approval dialog
3. Creates a `Scaffold` with the notebook model and extension host
4. Calls `InitializeSubsystems()` to populate themes, layouts, and settings
5. Subscribes to engine events (`OnVariablesChanged`, `OnExtensionStatusChanged`, etc.)
6. Warms up kernels in the background via `Task.Run`

Engine events are forwarded to the UI through `Action?` events on the service, which trigger Blazor's `StateHasChanged()` to re-render.

### Project References

Verso.Blazor references the engine (`Verso`), all kernel extension packages (`Verso.FSharp`, `Verso.JavaScript`, `Verso.Python`, `Verso.PowerShell`, `Verso.Ado`, `Verso.Http`), and the shared UI library (`Verso.Blazor.Shared`).

## VS Code Extension

The VS Code front-end uses a three-process architecture:

```
+--------------------------+      JSON-RPC       +------------------+
|  VS Code Extension       | <-- stdin/stdout -> |  Verso.Host.dll  |
|  (TypeScript, node.js)   |                     |  (.NET process)  |
+--------------------------+                     +------------------+
         |                                              |
         | postMessage                                  | Scaffold
         v                                              | ExtensionHost
+--------------------------+                            |
|  Blazor WASM Webview     |                     Engine runs here
|  (browser sandbox)       |
+--------------------------+
```

### Extension Entry Point

`extension.ts` activates by:

1. Resolving the path to `Verso.Host.dll`
2. Registering `BlazorEditorProvider` as a `CustomEditorProvider` for `.verso` files with `retainContextWhenHidden: true`
3. Conditionally registering Copilot chat participant and language model tools if the `vscode.chat` and `vscode.lm` APIs are available

### BlazorEditorProvider

When a `.verso` file is opened:

1. Creates a `VersoDocument` wrapping the file URI
2. Sets up the webview with `localResourceRoots` pointing to the bundled Blazor WASM assets
3. Spawns a dedicated `HostProcess` (one .NET process per open notebook)
4. Creates a `BlazorBridge` connecting the webview to that host
5. Sends `notebook/open` to the host to initialize the engine session
6. Notifies the WASM app that the notebook is ready

Each open notebook gets its own host process and bridge. When the editor panel is closed, the host process is terminated.

Save operations flow through the host: `saveCustomDocument` sends `notebook/save` to the host, receives serialized content, and writes it via `vscode.workspace.fs.writeFile`.

### HostProcess

`HostProcess` (TypeScript, `vscode/src/host/hostProcess.ts`) manages a `dotnet Verso.Host.dll` child process. Communication is line-delimited JSON-RPC over stdin/stdout:

- `start()` spawns the process and waits for the `host/ready` notification
- `sendRequest<T>(method, params)` writes a JSON-RPC request, stores a pending `Promise`, and resolves it when the response arrives
- `onNotification(method, handler)` registers callbacks for server-push notifications
- `dispose()` sends `host/shutdown` and force-kills after a timeout

### BlazorBridge

`BlazorBridge` (TypeScript, `vscode/src/blazor/blazorBridge.ts`) is the relay between the WASM webview and the host process:

- Listens for `jsonrpc-request` messages from the webview
- Injects the `notebookId` into every request
- Forwards to the host via `HostProcess.sendRequest()`
- Returns results to the webview as `jsonrpc-response` messages
- Forwards host notifications (`cell/executionState`, `variable/changed`, etc.) to the webview as `jsonrpc-notification` messages
- Intercepts a small set of operations locally (file save dialogs, user preferences)
- Tracks mutation methods and fires `onDidEdit()` for VS Code dirty-document tracking

### Blazor WASM (Verso.Blazor.Wasm)

The WASM project runs in the webview sandbox. It references only `Verso.Abstractions` and `Verso.Blazor.Shared`. It has no reference to the engine or any kernel project.

`RemoteNotebookService` implements `INotebookService` by forwarding all calls through `VsCodeBridge`, which calls into JavaScript interop (`vscode-bridge.js`) to post messages to the VS Code extension.

The WASM app maintains a local state cache populated from the `notebook/opened` notification. Source updates are debounced (250ms) before forwarding to the host to avoid flooding the JSON-RPC channel during fast typing.

`WebviewNavigationManager` stubs `NavigationManager` with `app:///` as the base URI because the `vscode-webview://` scheme is not parseable by `System.Uri`.

### Message Flow

A complete round-trip for a cell execution in VS Code:

```
Cell.razor calls NotebookService.ExecuteCellAsync(cellId)
  -> RemoteNotebookService calls VsCodeBridge.RequestAsync("execution/run", { cellId })
  -> VsCodeBridge calls JS vscodeBridge.sendRequest("execution/run", paramsJson)
  -> vscode-bridge.js posts { type: "jsonrpc-request", method, params } to VS Code
  -> BlazorBridge receives the message
  -> Enriches params with notebookId
  -> HostProcess.sendRequest("execution/run", enrichedParams)
  -> Writes JSON-RPC line to Verso.Host stdin
  -> HostSession dispatches to ExecutionHandler
  -> Scaffold.ExecuteCellAsync runs the pipeline
  -> Result written as JSON-RPC response to stdout
  -> HostProcess resolves the pending Promise
  -> BlazorBridge posts { type: "jsonrpc-response", result } to webview
  -> vscode-bridge.js resolves the pending Promise
  -> VsCodeBridge deserializes and returns to RemoteNotebookService
  -> RemoteNotebookService fires UI events
  -> Components re-render
```

Notifications (execution state changes, variable updates) flow in the reverse direction without a request ID.

## Verso.Host

`Verso.Host` is a console application that serves as the engine host for VS Code. It communicates via line-delimited JSON-RPC on stdin/stdout.

### Startup

`Program.cs` sets console encoding to UTF-8, emits a `host/ready` notification, then enters a read loop on stdin. A shared `stdoutLock` ensures atomic response writes. Incoming messages are queued through a `Channel<T>` and dispatched sequentially by `HostSession`.

### HostSession and NotebookSession

`HostSession` manages multiple concurrent notebook sessions keyed by generated IDs (`nb-1`, `nb-2`, etc.). `DispatchAsync` routes methods to typed handler classes via a large switch expression:

| Handler | Methods |
|---------|---------|
| `NotebookHandler` | open, close, save, getLanguages, getCellTypes, setFilePath |
| `CellHandler` | add, insert, remove, move, updateSource, changeType, changeLanguage |
| `ExecutionHandler` | run, runAll, cancel |
| `KernelHandler` | restart, getCompletions, getDiagnostics, getHoverInfo |
| `OutputHandler` | clearAll |
| `InteractionHandler` | interact (cell interaction dispatch) |
| `LayoutHandler` | getLayouts, switch, render, getCellContainer |
| `ThemeHandler` | getThemes, switch, getTheme |
| `ExtensionHandler` | list, enable, disable, consentResponse |
| `SettingsHandler` | getDefinitions, get, update, reset |
| `ToolbarHandler` | getEnabledStates, execute |
| `PropertiesHandler` | getSections, updateProperty, getSupported |
| `ParameterHandler` | list, add, update, remove |
| `VariableHandler` | list, inspect |

Method names are centralized in `Protocol.MethodNames`. The TypeScript `protocol.ts` in the VS Code extension mirrors the same method names.

## CLI (Verso.Cli)

The CLI is a .NET global tool with four commands. It references all kernel packages and the Blazor Server project.

### verso serve

Builds a Kestrel-hosted Blazor Server application via `BlazorHostBuilder`. This reuses the same `ServerNotebookService` and shared Razor components as the standalone `Verso.Blazor` project. The builder detects whether the static web assets manifest is valid (running from build output vs. installed as a global tool) and switches between Development and Production mode accordingly. In Production mode, bundled wwwroot files are served via `PhysicalFileProvider`.

### verso run

Creates a `HeadlessRunner` that builds a `Scaffold` and `ExtensionHost`, resolves parameters from `--param` flags (with type coercion against the notebook's parameter definitions), and executes cells sequentially. Output is rendered to the terminal via `OutputRenderer` (text mode) or written as structured JSON via `JsonOutputWriter`. Exit codes are deterministic for CI integration.

### verso convert

Uses the extension host's serializers to read a notebook in one format and write it in another. Supports `.verso`, `.ipynb`, and `.dib` as input formats. Output format is specified with `--to`. The `--strip-outputs` flag removes all cell outputs before writing.

### verso info

Displays the CLI version, .NET runtime version, engine version, and all discovered extensions, serializers, and formatters.
