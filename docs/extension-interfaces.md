# Extension Interfaces Reference

All Verso extensions implement one or more interfaces from `Verso.Abstractions`. Each interface extends `IExtension`, which provides identity, versioning, and lifecycle hooks. Classes must be decorated with `[VersoExtension]` to be discovered by the host.

This document covers every extension interface, its required members, lifecycle behavior, and links to reference implementations.

---

## IExtension

The base interface for all extensions. Every extension class must implement this, and every capability interface (`ILanguageKernel`, `ICellRenderer`, etc.) extends it.

### Members

| Member | Type | Description |
|---|---|---|
| `ExtensionId` | `string` | Unique identifier in reverse-domain format (e.g., `"com.mycompany.myext"`). |
| `Name` | `string` | Human-readable display name. |
| `Version` | `string` | Semantic version string (e.g., `"1.2.0"`). |
| `Author` | `string?` | Optional author or publisher name. |
| `Description` | `string?` | Optional short description of the extension. |
| `OnLoadedAsync(IExtensionHostContext)` | `Task` | Called when the host loads the extension. Use for initialization and service registration. |
| `OnUnloadedAsync()` | `Task` | Called when the host unloads the extension. Use for cleanup. |

### Lifecycle

1. The host scans assemblies for `[VersoExtension]`-attributed classes.
2. It instantiates each class and calls `OnLoadedAsync`, passing an `IExtensionHostContext`.
3. The extension remains active until `OnUnloadedAsync` is called (e.g., on host shutdown or extension disable).

### Example Implementation

See the Dice sample: `samples/SampleExtension/Verso.Sample.Dice/DiceExtension.cs`

---

## ILanguageKernel

Executes code, provides completions, diagnostics, and hover information for a specific language. Extends both `IExtension` and `IAsyncDisposable`.

### Members (in addition to IExtension)

| Member | Type | Description |
|---|---|---|
| `LanguageId` | `string` | Language identifier used to associate cells with this kernel (e.g., `"csharp"`, `"python"`). |
| `DisplayName` | `string` | Human-readable language name shown in UI selectors. |
| `FileExtensions` | `IReadOnlyList<string>` | File extensions for this language (e.g., `".cs"`, `".py"`). |
| `InitializeAsync()` | `Task` | One-time initialization called before any execution. |
| `ExecuteAsync(string, IExecutionContext)` | `Task<IReadOnlyList<CellOutput>>` | Executes source code and returns outputs. |
| `GetCompletionsAsync(string, int)` | `Task<IReadOnlyList<Completion>>` | Returns completion suggestions at the cursor position. |
| `GetDiagnosticsAsync(string)` | `Task<IReadOnlyList<Diagnostic>>` | Analyzes code and returns diagnostics (errors, warnings). |
| `GetHoverInfoAsync(string, int)` | `Task<HoverInfo?>` | Returns type/doc info for the symbol at the cursor. |
| `DisposeAsync()` | `ValueTask` | Releases kernel runtime resources (from `IAsyncDisposable`). |

### Lifecycle

1. `OnLoadedAsync` -- host registers the kernel.
2. `InitializeAsync` -- called once before first execution.
3. `ExecuteAsync` / `GetCompletionsAsync` / `GetDiagnosticsAsync` / `GetHoverInfoAsync` -- called as needed during notebook use.
4. `DisposeAsync` -- called on kernel restart or host shutdown.

### Example Implementation

- **Dice sample**: `samples/SampleExtension/Verso.Sample.Dice/DiceExtension.cs`
- **Built-in**: `CSharpKernel` in the `Verso` project

```csharp
[VersoExtension]
public sealed class DiceExtension : ILanguageKernel
{
    public string LanguageId => "dice";
    public string DisplayName => "Dice";
    public IReadOnlyList<string> FileExtensions => new[] { ".dice" };

    public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        // Parse dice notation and return results
        var outputs = new List<CellOutput>();
        // ... parsing logic ...
        return Task.FromResult<IReadOnlyList<CellOutput>>(outputs);
    }

    // ... other members ...
}
```

---

## ICellRenderer

Renders the input (editor) and output (result) areas of a cell. Each renderer is associated with a specific cell type via `CellTypeId`.

### Members (in addition to IExtension)

| Member | Type | Description |
|---|---|---|
| `CellTypeId` | `string` | Identifier of the cell type this renderer handles. |
| `DisplayName` | `string` | Human-readable name for this renderer. |
| `CollapsesInputOnExecute` | `bool` | Whether the input editor collapses after execution, showing only output. Defaults to `false`. |
| `RenderInputAsync(string, ICellRenderContext)` | `Task<RenderResult>` | Renders the cell's source code as visual content. |
| `RenderOutputAsync(CellOutput, ICellRenderContext)` | `Task<RenderResult>` | Renders a single execution output. |
| `GetEditorLanguage()` | `string?` | Returns the editor language ID for syntax highlighting, or `null`. |

### Lifecycle

Renderers are stateless. `RenderInputAsync` and `RenderOutputAsync` are called whenever the UI needs to display or refresh a cell. Use `ICellRenderContext` to access theme colors, cell metadata, and dimensions.

### Example Implementation

- **Dice sample**: `samples/SampleExtension/Verso.Sample.Dice/DiceRenderer.cs`
- **Built-in**: `MarkdownRenderer`

```csharp
[VersoExtension]
public sealed class DiceRenderer : ICellRenderer
{
    public string CellTypeId => "dice";
    public string DisplayName => "Dice";

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
    {
        var html = $"<pre><code>{HttpUtility.HtmlEncode(source)}</code></pre>";
        return Task.FromResult(new RenderResult("text/html", html));
    }

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
    {
        // Render output content
        return Task.FromResult(new RenderResult("text/html", output.Content));
    }

    public string? GetEditorLanguage() => null;
}
```

---

## IDataFormatter

Formats runtime objects into display outputs. The host selects the best formatter by checking `SupportedTypes`, `Priority`, and `CanFormat`.

### Members (in addition to IExtension)

| Member | Type | Description |
|---|---|---|
| `SupportedTypes` | `IReadOnlyList<Type>` | CLR types this formatter handles. Used for fast pre-filtering. |
| `Priority` | `int` | Conflict resolution priority. Higher values win. |
| `CanFormat(object, IFormatterContext)` | `bool` | Fine-grained check for whether this formatter can handle the value. |
| `FormatAsync(object, IFormatterContext)` | `Task<CellOutput>` | Produces a `CellOutput` for the given value. |

### Lifecycle

Formatters are stateless and invoked on-demand. When a kernel produces an object result, the host iterates registered formatters, filters by `SupportedTypes`, sorts by `Priority` (descending), and calls `CanFormat` then `FormatAsync` on the first match.

### Example Implementation

- **Dice sample**: `samples/SampleExtension/Verso.Sample.Dice/DiceFormatter.cs`
- **Built-in**: `PrimitiveFormatter`, `CollectionFormatter`, `ExceptionFormatter`, `HtmlFormatter`, `SvgFormatter`, `ImageFormatter`

```csharp
[VersoExtension]
public sealed class DiceFormatter : IDataFormatter
{
    public IReadOnlyList<Type> SupportedTypes => new[] { typeof(DiceResult) };
    public int Priority => 10;

    public bool CanFormat(object value, IFormatterContext context) => value is DiceResult;

    public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
    {
        var result = (DiceResult)value;
        var html = $"<table>...</table>"; // Build HTML table
        return Task.FromResult(new CellOutput("text/html", html));
    }
}
```

---

## IToolbarAction

Defines an action that appears on the notebook toolbar, cell toolbar, or context menu. Actions have enable/disable logic based on notebook state.

### Members (in addition to IExtension)

| Member | Type | Description |
|---|---|---|
| `ActionId` | `string` | Unique identifier for this action (e.g., `"dice.action.roll-all"`). |
| `DisplayName` | `string` | Label shown on the button or menu item. |
| `Icon` | `string?` | Optional icon name or path. |
| `Placement` | `ToolbarPlacement` | Where the action appears: `MainToolbar`, `CellToolbar`, or `ContextMenu`. |
| `Order` | `int` | Sort order within its placement group. Lower values appear first. |
| `IsEnabledAsync(IToolbarActionContext)` | `Task<bool>` | Whether the action is currently enabled. |
| `ExecuteAsync(IToolbarActionContext)` | `Task` | Performs the action. |

### ToolbarPlacement Enum

| Value | Description |
|---|---|
| `MainToolbar` | Primary toolbar at the top of the notebook. |
| `CellToolbar` | Inline toolbar within an individual cell. |
| `ContextMenu` | Right-click context menu. |

### Lifecycle

`IsEnabledAsync` is called when the UI refreshes (e.g., cell selection changes). `ExecuteAsync` is called when the user triggers the action.

### Example Implementation

- **Dice sample**: `samples/SampleExtension/Verso.Sample.Dice/DiceRollAction.cs`
- **Built-in**: `RunAllAction`, `RunCellAction`, `ClearOutputsAction`, `RestartKernelAction`, `ExportHtmlAction`, `ExportMarkdownAction`, `SwitchThemeAction`

```csharp
[VersoExtension]
public sealed class DiceRollAction : IToolbarAction
{
    public string ActionId => "dice.action.roll-all";
    public string DisplayName => "Roll All";
    public ToolbarPlacement Placement => ToolbarPlacement.MainToolbar;
    public int Order => 100;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var hasDiceCells = context.NotebookCells
            .Any(c => string.Equals(c.Language, "dice", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(hasDiceCells);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        foreach (var cell in context.NotebookCells.Where(c => c.Language == "dice"))
            await context.Notebook.ExecuteCellAsync(cell.Id);
    }
}
```

---

## IMagicCommand

Defines an inline directive invoked with a prefix such as `%time` or `#!nuget`. Magic commands extend kernel functionality without requiring a full UI component.

### Members (in addition to IExtension)

| Member | Type | Description |
|---|---|---|
| `Name` | `string` | Command name used for invocation (e.g., `"time"`, `"nuget"`). Shadows `IExtension.Name`. |
| `Description` | `string` | Short help text. Shadows `IExtension.Description`. |
| `Parameters` | `IReadOnlyList<ParameterDefinition>` | Parameter definitions for parsing and help generation. |
| `ExecuteAsync(string, IMagicCommandContext)` | `Task` | Executes the command with the raw argument string. |

### ParameterDefinition Record

| Property | Type | Description |
|---|---|---|
| `Name` | `string` | Parameter name as it appears in usage. |
| `Description` | `string` | Human-readable description. |
| `ParameterType` | `Type` | Expected CLR type for the value. |
| `IsRequired` | `bool` | Whether the parameter is mandatory. Default `false`. |
| `DefaultValue` | `object?` | Default when not supplied. Default `null`. |

### Lifecycle

Magic commands are parsed from cell source code before kernel execution. The command's `ExecuteAsync` runs first. If `context.SuppressExecution` is set to `true`, normal kernel execution is skipped.

### Example Implementation

- **Built-in**: `TimeMagicCommand`, `NuGetMagicCommand`, `RestartMagicCommand`, `ImportMagicCommand`

---

## ICellType

Defines a cell type by pairing a renderer with an optional language kernel. Cell types appear in the cell type picker.

### Members (in addition to IExtension)

| Member | Type | Description |
|---|---|---|
| `CellTypeId` | `string` | Unique cell type identifier (e.g., `"code-csharp"`, `"markdown"`). |
| `DisplayName` | `string` | Label shown in the cell type picker. |
| `Icon` | `string?` | Optional icon for menus. |
| `Renderer` | `ICellRenderer` | The renderer for cells of this type. |
| `Kernel` | `ILanguageKernel?` | Optional kernel for executable cell types. `null` for non-executable types. |
| `IsEditable` | `bool` | Whether the cell content can be edited. |
| `GetDefaultContent()` | `string` | Default source inserted when a new cell of this type is created. |

### Lifecycle

Cell types are registered at load time. The host uses `CellTypeId` to match cells to their renderer and kernel.

---

## INotebookSerializer

Serializes and deserializes notebooks to and from file formats. The host selects the serializer based on file extension.

### Members (in addition to IExtension)

| Member | Type | Description |
|---|---|---|
| `FormatId` | `string` | Format identifier (e.g., `"jupyter"`, `"verso-native"`). |
| `FileExtensions` | `IReadOnlyList<string>` | File extensions handled (e.g., `".ipynb"`, `".vnb"`), including the leading dot. |
| `SerializeAsync(NotebookModel)` | `Task<string>` | Converts a `NotebookModel` to its serialized string form. |
| `DeserializeAsync(string)` | `Task<NotebookModel>` | Parses serialized content into a `NotebookModel`. |
| `CanImport(string)` | `bool` | Checks if this serializer can import the file at the given path. |

### Lifecycle

Serializers are stateless. `DeserializeAsync` is called when opening a file; `SerializeAsync` when saving.

### Example Implementation

- **Built-in**: `JupyterSerializer`, `VersoSerializer`

---

## ILayoutEngine

Manages the spatial arrangement of cells. Layouts support different paradigms such as linear, grid, or freeform canvas.

### Members (in addition to IExtension)

| Member | Type | Description |
|---|---|---|
| `LayoutId` | `string` | Layout identifier (e.g., `"linear"`, `"grid"`). |
| `DisplayName` | `string` | Name shown in the layout picker. |
| `Icon` | `string?` | Optional icon. |
| `Capabilities` | `LayoutCapabilities` | Flags declaring supported operations. |
| `RequiresCustomRenderer` | `bool` | Whether the layout needs a custom rendering surface. |
| `RenderLayoutAsync(IReadOnlyList<CellModel>, IVersoContext)` | `Task<RenderResult>` | Renders the full layout for all cells. |
| `GetCellContainerAsync(Guid, IVersoContext)` | `Task<CellContainerInfo>` | Returns position/bounds for a specific cell. |
| `OnCellAddedAsync(Guid, int, IVersoContext)` | `Task` | Notification that a cell was added. |
| `OnCellRemovedAsync(Guid, IVersoContext)` | `Task` | Notification that a cell was removed. |
| `OnCellMovedAsync(Guid, int, IVersoContext)` | `Task` | Notification that a cell was moved. |
| `GetLayoutMetadata()` | `Dictionary<string, object>` | Returns layout state for persistence. |
| `ApplyLayoutMetadata(Dictionary<string, object>, IVersoContext)` | `Task` | Restores layout state from persisted metadata. |

### LayoutCapabilities Flags

| Flag | Value | Description |
|---|---|---|
| `None` | 0 | No capabilities. |
| `CellInsert` | 1 | Cells can be inserted. |
| `CellDelete` | 2 | Cells can be deleted. |
| `CellReorder` | 4 | Cells can be reordered. |
| `CellEdit` | 8 | Cell content is editable. |
| `CellResize` | 16 | Cells can be resized. |
| `CellExecute` | 32 | Cells can be executed. |
| `MultiSelect` | 64 | Multiple cells can be selected. |

### Example Implementation

- **Built-in**: `NotebookLayout`, `DashboardLayout`

---

## ITheme

Defines a complete visual theme including colors, typography, spacing, and syntax highlighting.

### Members (in addition to IExtension)

| Member | Type | Description |
|---|---|---|
| `ThemeId` | `string` | Theme identifier (e.g., `"verso-dark"`). |
| `DisplayName` | `string` | Name shown in the theme picker. |
| `ThemeKind` | `ThemeKind` | Whether the theme is `Light`, `Dark`, or `HighContrast`. |
| `Colors` | `ThemeColorTokens` | Color tokens for backgrounds, foregrounds, borders, and accents. |
| `Typography` | `ThemeTypography` | Font families, sizes, and line heights. |
| `Spacing` | `ThemeSpacing` | Padding, margin, and gap tokens. |
| `GetCustomToken(string)` | `string?` | Retrieves a custom design token by key. |
| `GetSyntaxColors()` | `SyntaxColorMap` | Returns syntax highlighting color mappings. |

### Example Implementation

- **Built-in**: `VersoDarkTheme`, `VersoLightTheme`

---

## INotebookPostProcessor

Hooks into the serialization pipeline to transform notebooks after deserialization and before serialization. Useful for cell injection, metadata migration, or format upgrades.

### Members (in addition to IExtension)

| Member | Type | Description |
|---|---|---|
| `Priority` | `int` | Execution order. Lower values run first. |
| `CanProcess(string?, string)` | `bool` | Whether this processor applies to the given file and format. |
| `PostDeserializeAsync(NotebookModel, string?)` | `Task<NotebookModel>` | Transforms the notebook after deserialization (on open). |
| `PreSerializeAsync(NotebookModel, string?)` | `Task<NotebookModel>` | Transforms the notebook before serialization (on save). |

### Lifecycle

Post-processors are sorted by `Priority` (ascending). `CanProcess` is checked first; if `true`, the transform method is called. Multiple processors form a chain -- each receives the output of the previous.

---

## Key Models

These records and classes are shared across all interfaces:

| Model | Description |
|---|---|
| `CellOutput(MimeType, Content, IsError, ErrorName, ErrorStackTrace)` | Output produced by cell execution. |
| `CellModel` | Mutable model with `Id`, `Type`, `Language`, `Source`, `Outputs`, `Metadata`. |
| `NotebookModel` | Full notebook document with cells, metadata, layout, and theme configuration. |
| `RenderResult(MimeType, Content)` | Rendered content returned by renderers. |
| `Completion(DisplayText, InsertText, Kind, Description, SortText)` | Code completion item. |
| `Diagnostic(Severity, Message, StartLine, StartColumn, EndLine, EndColumn, Code)` | Code diagnostic. |
| `HoverInfo(Content, MimeType, Range)` | Hover tooltip information. |
| `ParameterDefinition(Name, Description, ParameterType, IsRequired, DefaultValue)` | Magic command parameter definition. |
| `VariableDescriptor(Name, Value, Type, KernelId)` | Describes a stored variable. |

---

## See Also

- [Context Reference](context-reference.md) -- detailed reference for context interfaces passed to extension methods
- [Testing Extensions](testing-extensions.md) -- how to test each interface type
- [Best Practices](best-practices.md) -- naming conventions, thread safety, and performance
