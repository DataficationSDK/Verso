# Engine

The Verso engine is a headless .NET library that provides multi-language notebook execution, an extension system, cross-kernel variable sharing, and subsystem management. It has no UI dependencies and can be embedded in any .NET application.

## Scaffold

`Scaffold` (namespace `Verso`) is the central orchestrator for a notebook session. It owns the in-memory `NotebookModel`, the kernel registry, execution dispatch, and all subsystems. Every front-end creates a `Scaffold` instance to work with a notebook.

### Creating a Scaffold

```csharp
// Minimal: blank notebook, no extensions
var scaffold = new Scaffold();

// With a loaded notebook model
var scaffold = new Scaffold(notebookModel);

// Full: notebook, extension host, and file path
var extensionHost = new ExtensionHost();
await extensionHost.LoadBuiltInExtensionsAsync();
var scaffold = new Scaffold(notebookModel, extensionHost, filePath);
scaffold.InitializeSubsystems();
```

`InitializeSubsystems()` must be called after extensions are loaded. It queries the extension host for themes, layouts, and settable extensions, then creates the `ThemeEngine`, `LayoutManager`, and `SettingsManager`. It also hooks into `OnExtensionLoaded` and `OnExtensionStatusChanged` events so subsystems refresh automatically when extensions are added or toggled at runtime.

### Cell Management

Scaffold provides CRUD operations on the notebook's cell list:

| Method | Description |
|--------|-------------|
| `AddCell(type, language?, source?)` | Append a new cell |
| `InsertCell(index, type, language, source)` | Insert at a specific position |
| `RemoveCell(cellId)` | Remove by ID |
| `MoveCell(fromIndex, toIndex)` | Reorder |
| `GetCell(cellId)` | Look up by ID |
| `UpdateCellSource(cellId, source)` | Update source content |
| `ClearCells()` | Remove all cells |
| `ClearAllOutputs()` | Clear outputs, execution counts, and status from all cells |

Cell list mutations are synchronized with a lock to prevent concurrent modification.

### Kernel Registry

Kernels are stored in two places, consulted in priority order:

1. **Direct registry** -- a `Dictionary<string, ILanguageKernel>` populated via `RegisterKernel()`. These take precedence.
2. **Extension host** -- kernels discovered from loaded extensions via `ExtensionHost.GetKernels()`. Used as a fallback.

```csharp
scaffold.RegisterKernel(myKernel);           // Direct registration
var kernel = scaffold.GetKernel("csharp");   // Checks direct, then extension host
var languages = scaffold.RegisteredLanguages; // Union of both sources
```

Kernel initialization is lazy and thread-safe. The first call to execute a cell with a given kernel triggers `kernel.InitializeAsync()`. Concurrent callers share the same initialization task via a `ConcurrentDictionary<string, Task>`.

`WarmUpKernelAsync(languageId)` provides eager initialization, typically called at startup so IntelliSense is ready before the user runs their first cell.

`RestartKernelAsync(kernelId?)` disposes the kernel, clears the initialization task, clears the variable store, and re-warms the kernel.

### Execution

Three execution methods are available:

| Method | Description |
|--------|-------------|
| `ExecuteCellAsync(cellId, ct)` | Execute a single cell by ID |
| `ExecuteAllAsync(ct)` | Execute all cells in order |
| `ExecuteCodeAsync(code, language?, ct)` | Execute arbitrary code without adding a cell |

All execution methods inject notebook parameters into the variable store before the first cell runs. `ExecuteAllAsync` validates required parameters first. If validation fails, it executes the parameters cell (to preserve its form output), appends the validation error, and returns a single failed result.

Each call to `ExecuteCellAsync` creates a fresh `ExecutionPipeline` instance. The pipeline is not reused across cells. See [Execution Pipeline](execution-pipeline.md) for the full execution flow.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Cells` | `IReadOnlyList<CellModel>` | Current cell list |
| `Variables` | `IVariableStore` | Shared variable store |
| `Notebook` | `NotebookModel` | The underlying data model |
| `ThemeContext` | `IThemeContext` | Active theme or stub fallback |
| `LayoutCapabilities` | `LayoutCapabilities` | Active layout capabilities |
| `ExtensionHostContext` | `IExtensionHostContext` | Extension queries |
| `ThemeEngine` | `ThemeEngine?` | Nullable; requires `InitializeSubsystems()` |
| `LayoutManager` | `LayoutManager?` | Nullable; requires `InitializeSubsystems()` |
| `SettingsManager` | `SettingsManager?` | Nullable; requires `InitializeSubsystems()` |

### Lifecycle

`Scaffold` implements `IAsyncDisposable`. Disposing it disposes all registered kernels, clears internal registries, and disposes the extension host (which in turn unloads all extensions).

## Variable Store

`VariableStore` (namespace `Verso.Contexts`) is a thread-safe, case-insensitive key-value store backed by `ConcurrentDictionary<string, object>`. A single instance is shared across all kernels in a session.

### API

| Method | Description |
|--------|-------------|
| `Set(name, value)` | Upsert a variable. Fires `OnVariablesChanged`. |
| `Get<T>(name)` | Returns the value cast to `T`, or `default` if missing or wrong type. |
| `TryGet<T>(name, out value)` | Safe retrieval with type check. |
| `GetAll()` | Returns all variables as `VariableDescriptor(Name, Value, Type)`. |
| `Remove(name)` | Remove by name. Fires `OnVariablesChanged`. |
| `Clear()` | Remove all. Fires `OnVariablesChanged`. |

The `OnVariablesChanged` event fires after every mutation. Front-ends subscribe to this event to refresh the variable explorer UI.

### Cross-Kernel Sharing

All kernels receive the same `IVariableStore` instance through the context objects injected at execution time. There is no per-kernel isolation. A variable set by C# is immediately available to F#, Python, SQL, and every other kernel.

The typical patterns are:

- **Publish**: After execution, a kernel iterates its internal state and calls `Variables.Set()` for each variable. The C# kernel, for example, reads Roslyn's script state variables and publishes them.
- **Consume**: Before or during execution, a kernel reads from the store with `Variables.Get<T>()` or `TryGet<T>()`.
- **Side-channel**: Magic commands use the store to pass data to kernels. The `#!nuget` command writes resolved assembly paths under `__verso_nuget_assemblies`, which the C# kernel reads before compilation.

Notebook parameters are injected into the variable store before the first cell executes, using `ParameterValueParser` to coerce values to their declared CLR types.

## Theme Engine

`ThemeEngine` (namespace `Verso`) manages theme selection and implements `IThemeContext`. It holds a list of available themes and tracks the active one.

### API

| Method | Description |
|--------|-------------|
| `SetActiveTheme(themeId)` | Switch themes. Throws if not found. Fires `OnThemeChanged`. |
| `Refresh(updatedThemes)` | Called when extensions change. Preserves the active theme if still available. |

As `IThemeContext`, it delegates all token lookups to the active `ITheme`:

| Method | Description |
|--------|-------------|
| `ThemeKind` | `Light`, `Dark`, or `HighContrast` from the active theme |
| `GetColor(tokenName)` | Color token lookup via `ThemeColorTokens` |
| `GetFont(fontRole)` | Font lookup via `ThemeTypography` |
| `GetSpacing(spacingName)` | Spacing lookup via `ThemeSpacing` |
| `GetSyntaxColor(tokenType)` | Syntax highlighting color from the theme's `SyntaxColorMap` |
| `GetCustomToken(key)` | Custom theme-defined token |

When no theme is active, all lookups fall back to default instances of `ThemeColorTokens`, `ThemeTypography`, and `ThemeSpacing`.

## Layout Manager

`LayoutManager` (namespace `Verso`) manages layout selection and provides layout-related operations.

### API

| Method | Description |
|--------|-------------|
| `SetActiveLayout(layoutId)` | Switch layouts. Throws if not found. Fires `OnLayoutChanged`. |
| `Refresh(updatedLayouts)` | Called when extensions change. Preserves the active layout if available; otherwise falls back to the first non-custom-renderer layout. |
| `SaveMetadataAsync(notebook)` | Persists each layout's metadata into `notebook.Layouts`. |
| `RestoreMetadataAsync(notebook, context)` | Restores layout metadata from the notebook model. |

Properties include `ActiveLayout`, `AvailableLayouts`, `RequiresCustomRenderer` (delegates to active layout), and `Capabilities`.

## Notebook Model

`NotebookModel` is the in-memory document. Key fields:

| Field | Type | Description |
|-------|------|-------------|
| `FormatVersion` | `string` | Always `"1.0"` |
| `Title` | `string?` | Notebook title |
| `DefaultKernelId` | `string?` | Default language for new code cells |
| `Cells` | `List<CellModel>` | Ordered cell list |
| `Parameters` | `Dictionary<string, NotebookParameterDefinition>?` | Typed parameter definitions |
| `Layouts` | `Dictionary<string, Dictionary<string, object>>` | Per-layout metadata |
| `ExtensionSettings` | `Dictionary<string, Dictionary<string, object?>>` | Per-extension settings |
| `RequiredExtensions` | `List<string>?` | Extension IDs that must be present |
| `ActiveLayoutId` | `string?` | Current layout |
| `PreferredThemeId` | `string?` | Preferred theme |

`CellModel` fields:

| Field | Type | Serialized | Description |
|-------|------|:----------:|-------------|
| `Id` | `Guid` | Yes | Unique cell identifier |
| `Type` | `string` | Yes | Cell type (e.g. `"code"`, `"markdown"`, `"parameters"`) |
| `Language` | `string?` | Yes | Language for code cells |
| `Source` | `string` | Yes | Cell content |
| `Outputs` | `List<CellOutput>` | Yes | Execution outputs |
| `Metadata` | `Dictionary<string, object>` | Yes | Arbitrary key-value metadata |
| `ExecutionCount` | `int?` | No | Monotonic counter per cell |
| `LastElapsed` | `TimeSpan?` | No | Duration of last execution |
| `LastStatus` | `ExecutionStatus?` | No | Result of last execution |

## Serialization

Notebooks are serialized and deserialized through the `INotebookSerializer` interface. Three built-in serializers ship as extensions:

| Serializer | Format ID | Extensions | Read | Write |
|------------|-----------|------------|:----:|:-----:|
| `VersoSerializer` | `verso.serializer.verso` | `.verso` | Yes | Yes |
| `JupyterSerializer` | `verso.serializer.jupyter` | `.ipynb` | Yes | No |
| `DibSerializer` | `verso.serializer.dib` | `.dib` | Yes | No |

The `.verso` format is native JSON. Jupyter and Polyglot Notebook (`.dib`) files are import-only. Saving always writes `.verso` format.

The `VersoSerializer` intentionally omits parameters cell outputs during serialization because those outputs are always re-rendered from `metadata.parameters` at display time.

Post-processors (`INotebookPostProcessor`) can transform the notebook model after deserialization or before serialization. They are sorted by `Priority` and applied in order.
