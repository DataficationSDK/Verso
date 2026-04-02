# Execution Pipeline

The execution pipeline handles the complete flow from "run this cell" to "here are the outputs." It resolves which kernel or renderer handles the cell, processes magic commands, runs the code, collects outputs, and reports results.

## Entry Points

Execution starts from one of three methods on `Scaffold`:

| Method | Description |
|--------|-------------|
| `ExecuteCellAsync(cellId, ct)` | Execute a single cell by ID |
| `ExecuteAllAsync(ct)` | Execute all cells in notebook order |
| `ExecuteCodeAsync(code, language?, ct)` | Execute arbitrary code in a transient cell (not added to the notebook) |

All three methods call `EnsureParametersInjected()` first, which pushes notebook parameter defaults into the variable store for any parameters not already present. Each call to `ExecuteCellAsync` creates a fresh `ExecutionPipeline` instance via `Scaffold.BuildPipeline()`. The pipeline is not reused across cells.

`ExecuteAllAsync` validates required parameters before executing. If any required parameter (marked `required: true`) has no default and no value in the variable store, the method executes only the parameters cell (to preserve its form output), appends the validation error, and returns a single failed result.

## Pipeline Construction

`Scaffold.BuildPipeline()` creates an `ExecutionPipeline` by injecting all dependencies as constructor arguments:

- `IVariableStore` -- the shared variable store
- `IThemeContext` -- active theme for context
- `LayoutCapabilities` -- active layout capabilities
- `IExtensionHostContext` -- for querying extensions
- `INotebookMetadata` -- read-only notebook metadata
- `INotebookOperations` -- for notebook manipulation from magic commands
- `Func<string, ILanguageKernel?>` -- kernel resolution delegate
- `Func<ILanguageKernel, Task>` -- kernel initialization delegate
- `Func<Guid, string?>` -- language ID resolution for a cell
- `Func<Guid, int>` -- execution count lookup
- `Func<string, IMagicCommand?>` -- magic command resolution delegate

The pipeline has no direct reference to `Scaffold`. All access goes through these delegates and interfaces.

## Cell Routing

When `ExecuteAsync(cell, ct)` is called, the pipeline determines how to handle the cell through a priority-based dispatch:

### Step 1: Check ICellType

Query `IExtensionHostContext.GetCellTypes()` for a match on `cell.Type` (case-insensitive).

- If a matching `ICellType` is found **and** `cellType.Kernel is not null`: route to `ExecuteWithKernelAsync` using that kernel.
- If a matching `ICellType` is found **and** `cellType.Kernel is null`: route to `RenderCellAsync` using `cellType.Renderer`. This path is used by non-executable cells like parameters, which have a renderer but no kernel.

### Step 2: Check Cell Language

If no `ICellType` matched and `cell.Language` is set, look up the kernel via the resolution delegate. If found, route to `ExecuteWithKernelAsync`.

### Step 3: Check Free-Standing Renderers

Query `IExtensionHostContext.GetRenderers()` for a renderer whose `CellTypeId` matches `cell.Type`. If found, route to `RenderCellAsync`.

### Step 4: Default Kernel

As a last resort, resolve the notebook-level default language for this cell and look up that kernel. If found, route to `ExecuteWithKernelAsync`. If nothing matches at any step, the pipeline throws `InvalidOperationException`.

## Kernel Execution Flow

`ExecuteWithKernelAsync` handles cells that have a language kernel:

### 1. Initialize the Kernel

Call the initialization delegate, which uses `ConcurrentDictionary<string, Task>` to ensure `kernel.InitializeAsync()` runs exactly once per session. Concurrent callers share the same task.

### 2. Clear Previous Outputs

`cell.Outputs.Clear()` removes any outputs from the previous execution.

### 3. Set Up Output Collection

A thread-safe `AppendOutput` delegate is created, backed by a lock. Outputs added via this delegate are tracked in a `HashSet<CellOutput>` (by reference) to avoid duplicates. This delegate is passed to both magic command contexts and execution contexts.

### 4. Process Magic Commands

The pipeline runs a loop to process consecutive magic commands at the top of the cell:

```
while (true) {
    parseResult = MagicCommandParser.Parse(currentSource);
    if (!parseResult.IsMagicCommand) break;

    // Resolve and execute the magic command
    magicCommand.ExecuteAsync(arguments, magicContext);

    if (magicContext.SuppressExecution) return Success;
    currentSource = parseResult.RemainingCode;
}
```

See the [Magic Commands](#magic-commands) section below for details.

### 5. Build Execution Context

An `ExecutionContext` is constructed with the remaining code (after magic commands are stripped), the variable store, theme context, and all other shared services. This context implements `IExecutionContext`, which extends `IVersoContext`.

### 6. Execute

The kernel's `ExecuteAsync(code, context)` is called. The kernel can:

- **Stream outputs** during execution via `context.WriteOutputAsync(output)`, which calls the `AppendOutput` delegate
- **Return outputs** from the method, which are appended after execution (skipping any already streamed)

### 7. Post-Processing

If the `#!time` magic command was used, a wall-time output (`"Wall time: X.Xms"`) is appended. The method returns `ExecutionResult.Success` with the cell ID, execution count, and elapsed time.

### Error Handling

- `OperationCanceledException` returns `ExecutionResult.Cancelled`
- Any other exception appends a `CellOutput` with `IsError: true`, `ErrorName`, and `ErrorStackTrace`, then returns `ExecutionResult.Failed`

## Render-Only Flow

`RenderCellAsync` handles cells that have a renderer but no kernel (like parameters and rich content cells):

1. Clear `cell.Outputs`
2. Build a `CellRenderContext` with the cell's metadata, variable store, and theme context
3. Call `renderer.RenderInputAsync(cell.Source, renderContext)`
4. Append the `RenderResult` as a `CellOutput`
5. Return `ExecutionResult.Success`

## Magic Commands

Magic commands are `#!` directives at the top of a cell that modify execution behavior or perform side effects before the kernel runs.

### Parsing

`MagicCommandParser.Parse(source)` scans the first non-empty line. If it starts with `#!`, the parser extracts:

- **CommandName**: the first token after `#!` (e.g., `nuget`, `time`, `import`)
- **Arguments**: everything after the command name on the same line
- **RemainingCode**: all lines after the magic command line

The parser returns a `ParseResult` record. The pipeline calls it in a loop to handle consecutive magic commands (e.g., two `#!extension` lines stacked at the top of a cell).

### Resolution

The pipeline resolves magic commands via the delegate from `Scaffold`, which queries `ExtensionHost.GetMagicCommands()` for a case-insensitive name match. If the command is not found, the pipeline checks whether it exists in a disabled extension (to distinguish "unknown command" from "disabled extension") and emits an appropriate error.

### Context

Each magic command receives a `MagicCommandContext` that provides:

- `RemainingCode` -- the code after the magic command line
- `SuppressExecution` -- settable by the command to prevent kernel execution
- All `IVersoContext` services (variable store, theme, extension host, notebook operations)

Internally, the context also has `ReportElapsedTime`, which the `#!time` command sets so the pipeline appends timing information after kernel execution.

### Built-In Magic Commands

| Command | Effect |
|---------|--------|
| `#!about` | Outputs version, runtime, and extension info. Suppresses execution. |
| `#!time` | Enables elapsed time reporting after kernel execution. Does not suppress. |
| `#!nuget PackageId [Version]` | Resolves a NuGet package and stores assembly paths in the variable store for the kernel to pick up. |
| `#!extension PackageId\|path [Version]` | Loads an extension from NuGet or a local DLL. Requests consent for NuGet packages. Idempotent. |
| `#!import path [--param name=value ...]` | Deserializes and executes another notebook's code cells with optional parameter overrides. Suppresses execution. |
| `#!restart` | Restarts the current kernel. |

Language-specific magic commands are provided by their respective extensions:

| Command | Extension | Effect |
|---------|-----------|--------|
| `#!pip package` | Verso.Python | Installs a Python package |
| `#!npm package` | Verso.JavaScript | Installs an npm package |
| `#!connect` | Verso.Ado | Establishes a database connection |

## Output Model

`CellOutput` is an immutable record:

```csharp
record CellOutput(
    string MimeType,
    string Content,
    bool IsError = false,
    string? ErrorName = null,
    string? ErrorStackTrace = null
);
```

Outputs are collected in `cell.Outputs` (a mutable `List<CellOutput>`). The pipeline clears this list at the start of each execution. Kernels can produce multiple outputs of different MIME types. Common patterns:

| MIME Type | Content |
|-----------|---------|
| `text/plain` | Plain text output |
| `text/html` | Rich HTML (tables, formatted output) |
| `image/svg+xml` | SVG graphics |
| `image/png`, `image/jpeg` | Base64-encoded image data |
| `application/json` | JSON data (rendered as interactive tree in UI) |
| `text/csv` | CSV data (rendered as table in UI) |
| `text/x-verso-mermaid` | Mermaid diagram source (rendered by the UI) |

Error outputs set `IsError = true` and optionally include `ErrorName` (exception type) and `ErrorStackTrace`.

## Execution Result

`ExecutionResult` is an immutable record returned by the pipeline:

```csharp
enum ExecutionStatus { Success, Cancelled, Failed }

record ExecutionResult {
    ExecutionStatus Status;
    Guid CellId;
    int ExecutionCount;
    TimeSpan Elapsed;
    Exception? Error;
}
```

Factory methods `Success(...)`, `Cancelled(...)`, and `Failed(...)` construct the appropriate result. The `CellId` and `ExecutionCount` allow the caller to correlate results with cells. `Elapsed` is the wall-clock time of the execution.

## Cell Types and Renderers

### ICellType

`ICellType` bundles a renderer with an optional kernel to define a complete cell type:

| Member | Description |
|--------|-------------|
| `CellTypeId` | Matches `CellModel.Type` (e.g., `"parameters"`, `"html"`, `"mermaid"`) |
| `Renderer` | The `ICellRenderer` that renders this cell type |
| `Kernel` | Optional `ILanguageKernel`; null for non-executable types |
| `IsEditable` | Whether the user can edit the cell source |
| `GetDefaultContent()` | Template content for newly created cells of this type |

### ICellRenderer

`ICellRenderer` transforms cell content into visual output:

| Member | Description |
|--------|-------------|
| `CellTypeId` | Matches the cell type this renderer handles |
| `RenderInputAsync(source, context)` | Renders the cell source into HTML |
| `RenderOutputAsync(output, context)` | Re-renders a stored output |
| `GetEditorLanguage()` | Syntax highlighting language for the editor (null if no editor) |
| `CollapsesInputOnExecute` | If true, the source editor is hidden after execution (used by markdown) |

### ICellInteractionHandler

Renderers that produce interactive HTML (like the parameters form) can also implement `ICellInteractionHandler` to receive user interactions back from the browser:

```csharp
Task<string?> OnCellInteractionAsync(CellInteractionContext context);
```

The `CellInteractionContext` carries the cell ID, extension ID, interaction type, and a JSON payload. The handler returns updated HTML (or null) which replaces the cell's output in the UI.

## Data Formatters

When a kernel produces a runtime object rather than a raw string output, the engine uses `IDataFormatter` extensions to convert it into a `CellOutput`. Formatters are sorted by `Priority` (higher values take precedence) and the first formatter that returns `true` from `CanFormat(value, context)` is used.

Built-in formatters handle primitives, collections (as HTML tables), HTML strings, images, SVG, exceptions, F# types, and SQL result sets.
