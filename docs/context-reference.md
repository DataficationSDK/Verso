# Context Reference

Every Verso extension method receives a context object that provides access to shared services, notebook state, and operation-specific data. All context interfaces extend `IVersoContext`, which defines the common surface.

This document covers each context interface, its members, when it is available, and the helper interfaces it exposes.

---

## IVersoContext

The base context available to all extension operations. Every specialized context (`IExecutionContext`, `IFormatterContext`, etc.) inherits these members.

### Members

| Member | Type | Description |
|---|---|---|
| `Variables` | `IVariableStore` | Shared variable store for exchanging data between kernels. |
| `CancellationToken` | `CancellationToken` | Signals when the current operation should be aborted. Always check this in long-running work. |
| `Theme` | `IThemeContext` | Read-only access to the active visual theme (colors, fonts, spacing). |
| `LayoutCapabilities` | `LayoutCapabilities` | Flags describing what the current rendering surface supports (cell insert, resize, etc.). |
| `ExtensionHost` | `IExtensionHostContext` | Query loaded extensions by category. |
| `NotebookMetadata` | `INotebookMetadata` | Read-only notebook-level metadata (title, default kernel, file path). |
| `Notebook` | `INotebookOperations` | Execute cells, manage outputs, insert/remove/move cells, switch layouts and themes. |
| `WriteOutputAsync(CellOutput)` | `Task` | Writes a cell output to the notebook output stream. |
| `RequestFileDownloadAsync(string, string, byte[])` | `Task` | Requests the host to deliver a file download to the user. Default implementation throws `NotSupportedException` -- only available on hosts that support downloads. |

### When Available

`IVersoContext` members are available in every extension method that receives a context. You never instantiate it directly; the host provides it.

### Usage Example

```csharp
public async Task ExecuteAsync(string code, IExecutionContext context)
{
    // Check cancellation
    context.CancellationToken.ThrowIfCancellationRequested();

    // Read a shared variable
    var lastResult = context.Variables.Get<int>("counter");

    // Write output
    await context.WriteOutputAsync(new CellOutput("text/plain", "Hello from the kernel"));

    // Query loaded extensions
    var kernels = context.ExtensionHost.GetKernels();
}
```

---

## IExecutionContext

Extends `IVersoContext` with execution-specific state. Passed to `ILanguageKernel.ExecuteAsync`.

### Additional Members

| Member | Type | Description |
|---|---|---|
| `CellId` | `Guid` | Unique identifier of the cell being executed. |
| `ExecutionCount` | `int` | Monotonically increasing execution counter for the current cell. |
| `DisplayAsync(CellOutput)` | `Task` | Sends a display output that can be updated in place during execution. |

### When Available

Only within `ILanguageKernel.ExecuteAsync`. Not available during completions, diagnostics, or hover queries.

### DisplayAsync vs. WriteOutputAsync

- `WriteOutputAsync` (from `IVersoContext`) appends output to the cell's output list permanently.
- `DisplayAsync` sends a live-updating display that can be replaced. Use it for progress indicators, streaming output, or interactive displays.

### Usage Example

```csharp
public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
{
    // Show progress
    await context.DisplayAsync(new CellOutput("text/plain", "Working..."));

    // Store a result in the variable store for other kernels
    context.Variables.Set("_lastRoll", result);

    return new[] { new CellOutput("text/plain", result.ToString()) };
}
```

### Test Stub

`StubExecutionContext` from `Verso.Testing.Stubs` tracks both `WrittenOutputs` and `DisplayedOutputs` as `List<CellOutput>` for assertion.

---

## IFormatterContext

Extends `IVersoContext` with formatting constraints. Passed to `IDataFormatter.CanFormat` and `IDataFormatter.FormatAsync`.

### Additional Members

| Member | Type | Description |
|---|---|---|
| `MimeType` | `string` | Target MIME type for the output (e.g., `"text/html"`, `"text/plain"`). |
| `MaxWidth` | `double` | Maximum available width for the formatted output, in device-independent units. |
| `MaxHeight` | `double` | Maximum available height for the formatted output, in device-independent units. |

### When Available

Only within `IDataFormatter.CanFormat` and `IDataFormatter.FormatAsync`.

### Usage Example

```csharp
public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
{
    if (context.MimeType == "text/plain")
        return Task.FromResult(new CellOutput("text/plain", value.ToString()!));

    // Respect size constraints
    var style = $"max-width:{context.MaxWidth}px;max-height:{context.MaxHeight}px;overflow:auto;";
    var html = $"<div style=\"{style}\">...</div>";
    return Task.FromResult(new CellOutput("text/html", html));
}
```

### Test Stub

`StubFormatterContext` defaults to `MimeType = "text/html"`, `MaxWidth = 800`, `MaxHeight = 600`. All properties are settable.

---

## ICellRenderContext

Extends `IVersoContext` with cell-specific rendering information. Passed to `ICellRenderer.RenderInputAsync` and `ICellRenderer.RenderOutputAsync`.

### Additional Members

| Member | Type | Description |
|---|---|---|
| `CellId` | `Guid` | Unique identifier of the cell being rendered. |
| `CellMetadata` | `IReadOnlyDictionary<string, object>` | Read-only metadata dictionary attached to the cell. |
| `Dimensions` | `(double Width, double Height)` | Available rendering dimensions in device-independent units. |
| `IsSelected` | `bool` | Whether the cell is currently selected in the notebook UI. |

### When Available

Only within `ICellRenderer.RenderInputAsync` and `ICellRenderer.RenderOutputAsync`.

### Usage Example

```csharp
public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
{
    // Adapt rendering to selection state
    var borderColor = context.IsSelected
        ? context.Theme.GetColor("accent")
        : context.Theme.GetColor("border");

    var html = $"<div style=\"border:1px solid {borderColor};\">{source}</div>";
    return Task.FromResult(new RenderResult("text/html", html));
}
```

### Test Stub

`StubCellRenderContext` defaults to `Dimensions = (800, 600)`, `IsSelected = false`, and an empty `CellMetadata` dictionary. All properties are settable.

---

## IMagicCommandContext

Extends `IVersoContext` with magic command-specific state. Passed to `IMagicCommand.ExecuteAsync`.

### Additional Members

| Member | Type | Description |
|---|---|---|
| `RemainingCode` | `string` | The cell source code that follows the magic command directive. |
| `SuppressExecution` | `bool` (get/set) | Set to `true` to prevent normal kernel execution after the magic command completes. |

### When Available

Only within `IMagicCommand.ExecuteAsync`.

### Usage Example

```csharp
public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
{
    // The "time" magic measures execution of the remaining code
    var sw = Stopwatch.StartNew();
    // Let the kernel execute the remaining code normally
    // (SuppressExecution defaults to false)
    await Task.CompletedTask;
    sw.Stop();

    await context.WriteOutputAsync(
        new CellOutput("text/plain", $"Elapsed: {sw.Elapsed}"));
}
```

### Setting SuppressExecution

When a magic command fully handles execution (e.g., `#!restart`), set `SuppressExecution = true` so the kernel does not attempt to execute `RemainingCode`:

```csharp
public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
{
    await context.Notebook.RestartKernelAsync();
    context.SuppressExecution = true; // Skip normal execution
}
```

### Test Stub

`StubMagicCommandContext` defaults to `RemainingCode = ""` and `SuppressExecution = false`. It tracks `WrittenOutputs` as a `List<CellOutput>`.

---

## IToolbarActionContext

Extends `IVersoContext` with toolbar action-specific state. Passed to `IToolbarAction.IsEnabledAsync` and `IToolbarAction.ExecuteAsync`.

### Additional Members

| Member | Type | Description |
|---|---|---|
| `SelectedCellIds` | `IReadOnlyList<Guid>` | Identifiers of the currently selected cells. |
| `NotebookCells` | `IReadOnlyList<CellModel>` | All cells in the notebook, in order. |
| `ActiveKernelId` | `string?` | Identifier of the currently active language kernel, or `null`. |

### When Available

Within `IToolbarAction.IsEnabledAsync` (called on UI refresh) and `IToolbarAction.ExecuteAsync` (called on user trigger).

### Usage Example

```csharp
public Task<bool> IsEnabledAsync(IToolbarActionContext context)
{
    // Only enable if there are dice cells
    var hasDiceCells = context.NotebookCells
        .Any(c => string.Equals(c.Language, "dice", StringComparison.OrdinalIgnoreCase));
    return Task.FromResult(hasDiceCells);
}

public async Task ExecuteAsync(IToolbarActionContext context)
{
    foreach (var cell in context.NotebookCells.Where(c => c.Language == "dice"))
        await context.Notebook.ExecuteCellAsync(cell.Id);
}
```

### File Downloads

`IToolbarActionContext` inherits `RequestFileDownloadAsync` from `IVersoContext`. The `StubToolbarActionContext` tracks downloads in its `DownloadedFiles` list:

```csharp
public async Task ExecuteAsync(IToolbarActionContext context)
{
    var bytes = Encoding.UTF8.GetBytes("<html>...</html>");
    await context.RequestFileDownloadAsync("export.html", "text/html", bytes);
}
```

### Test Stub

`StubToolbarActionContext` provides settable `SelectedCellIds`, `NotebookCells`, and `ActiveKernelId`. It tracks `WrittenOutputs` and `DownloadedFiles` for assertion.

---

## Helper Interfaces

These interfaces are accessed through `IVersoContext` properties rather than passed directly.

### IVariableStore

Accessed via `context.Variables`. Provides shared variable storage for exchanging data between kernels within a notebook session.

| Member | Type | Description |
|---|---|---|
| `OnVariablesChanged` | `event Action?` | Raised after `Set`, `Remove`, or `Clear`. |
| `Set(string, object)` | `void` | Stores a variable, replacing any existing one with the same name. |
| `Get<T>(string)` | `T?` | Retrieves a variable by name, cast to `T`. Returns `default` if not found. |
| `TryGet<T>(string, out T?)` | `bool` | Attempts to retrieve and cast a variable. |
| `GetAll()` | `IReadOnlyList<VariableDescriptor>` | Returns descriptors for all stored variables. |
| `Remove(string)` | `bool` | Removes a variable by name. Returns `true` if found. |
| `Clear()` | `void` | Removes all variables. |

### IThemeContext

Accessed via `context.Theme`. Provides read-only access to the active visual theme. See [Best Practices](best-practices.md) for theme-aware rendering patterns.

| Member | Type | Description |
|---|---|---|
| `ThemeKind` | `ThemeKind` | Current theme kind: `Light`, `Dark`, or `HighContrast`. |
| `GetColor(string)` | `string` | Resolves a named color token to its value (e.g., hex code). |
| `GetFont(string)` | `FontDescriptor` | Retrieves the font descriptor for a role (e.g., `"body"`, `"code"`). |
| `GetSpacing(string)` | `double` | Resolves a spacing token to its value in device-independent units. |
| `GetSyntaxColor(string)` | `string?` | Resolves a syntax token type to its color, or `null`. |
| `GetCustomToken(string)` | `string?` | Retrieves a custom theme token value by key. |

### INotebookMetadata

Accessed via `context.NotebookMetadata`. Read-only notebook-level metadata.

| Member | Type | Description |
|---|---|---|
| `Title` | `string?` | Notebook title, or `null`. |
| `DefaultKernelId` | `string?` | Default kernel for new cells, or `null`. |
| `FilePath` | `string?` | File path on disk, or `null` if unsaved. |

### INotebookOperations

Accessed via `context.Notebook`. Provides centralized notebook-level operations.

| Member | Type | Description |
|---|---|---|
| `ExecuteCellAsync(Guid)` | `Task` | Executes a single cell. |
| `ExecuteAllAsync()` | `Task` | Executes all cells in order. |
| `ExecuteFromAsync(Guid)` | `Task` | Executes from the specified cell to the end. |
| `ClearOutputAsync(Guid)` | `Task` | Clears a single cell's outputs. |
| `ClearAllOutputsAsync()` | `Task` | Clears all cell outputs. |
| `RestartKernelAsync(string?)` | `Task` | Restarts the specified or default kernel. |
| `InsertCellAsync(int, string, string?)` | `Task<string>` | Inserts a new cell at the given index. |
| `RemoveCellAsync(Guid)` | `Task` | Removes a cell. |
| `MoveCellAsync(Guid, int)` | `Task` | Moves a cell to a new position. |
| `ExecuteCodeAsync(string, string?, CancellationToken)` | `Task` | Executes code in a kernel without a visible cell. |
| `ActiveLayoutId` | `string?` | Currently active layout identifier. |
| `SetActiveLayout(string)` | `void` | Switches the active layout. |
| `ActiveThemeId` | `string?` | Currently active theme identifier. |
| `SetActiveTheme(string)` | `void` | Switches the active theme. |

### IExtensionHostContext

Accessed via `context.ExtensionHost`. Query loaded extensions by category.

| Member | Type | Description |
|---|---|---|
| `GetLoadedExtensions()` | `IReadOnlyList<IExtension>` | All loaded extensions. |
| `GetKernels()` | `IReadOnlyList<ILanguageKernel>` | All registered kernels. |
| `GetRenderers()` | `IReadOnlyList<ICellRenderer>` | All registered renderers. |
| `GetFormatters()` | `IReadOnlyList<IDataFormatter>` | All registered formatters. |
| `GetCellTypes()` | `IReadOnlyList<ICellType>` | All registered cell types. |
| `GetSerializers()` | `IReadOnlyList<INotebookSerializer>` | All registered serializers. |
| `GetLayouts()` | `IReadOnlyList<ILayoutEngine>` | All registered layouts. |
| `GetThemes()` | `IReadOnlyList<ITheme>` | All registered themes. |
| `GetPostProcessors()` | `IReadOnlyList<INotebookPostProcessor>` | All registered post-processors. |
| `GetExtensionInfos()` | `IReadOnlyList<ExtensionInfo>` | Metadata for all loaded extensions. |
| `EnableExtensionAsync(string)` | `Task` | Enables a previously disabled extension. |
| `DisableExtensionAsync(string)` | `Task` | Disables an extension without unloading it. |

---

## See Also

- [Extension Interfaces](extension-interfaces.md) -- the interfaces that receive these contexts
- [Testing Extensions](testing-extensions.md) -- stub implementations for each context
- [Best Practices](best-practices.md) -- theme-aware rendering and cancellation patterns
