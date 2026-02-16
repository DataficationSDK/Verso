# Known Issues

> Tracked issues and workarounds for the Verso notebook engine.

---

## VERSO-001: Browse file picker breaks relative imports in Blazor

| | |
|---|---|
| **Status** | Fixed |
| **Affected** | Verso.Blazor |
| **Severity** | Medium |
| **Fixed in** | `NotebookService.cs` |

### Symptom

When opening a `.verso` file via the "Browse" button (`<InputFile>` picker), relative imports such as `#!import ./helpers.verso` fail with a path like:

```
File not found: /Users/.../src/Verso.Blazor/helpers.verso
```

Opening the same file by pasting its path into the "Open" text input works correctly.

### Root cause

The browser's security model strips the full directory path from file picker selections — only the filename is available. `OpenFromContentAsync` created a `Scaffold` without a `filePath`, so `ImportMagicCommand.ResolvePath` fell back to `Directory.GetCurrentDirectory()` (the Blazor project directory) instead of the notebook's actual directory.

### Fix

`NotebookService.OpenFromContentAsync` now calls `TryResolveFilePathAsync` before falling back to the content-only path. The resolver searches the last-opened directory and CWD (up to 5 levels deep) for a file matching both name and content, then delegates to `OpenAsync(resolvedPath)` which sets the path correctly on the `Scaffold`.

### Workaround (pre-fix)

Paste or type the full file path into the "Open" text input instead of using "Browse".

---

## VERSO-002: Cell type switching and registered cell types not exposed in Blazor UI

| | |
|---|---|
| **Status** | In Development |
| **Affected** | Verso.Blazor |
| **Severity** | Low |

### Symptom

The Blazor toolbar only offers "+ Code" and "+ Markdown" buttons. Cell types registered through extensions (e.g. SQL via `Verso.Ado`) cannot be created from the UI — they can only be loaded by opening a `.verso` file that already contains them. Additionally, there is no way to change a cell's type after creation.

### Root cause

The toolbar in `Toolbar.razor` hard-codes two buttons that emit `"code"` and `"markdown"` type strings. There is no dynamic cell type picker that queries the `ICellType` registry from `ExtensionHost.GetCellTypes()`, and no cell-conversion UI exists.

### Planned fix

A cell type dropdown/picker that enumerates all registered `ICellType` extensions at runtime, including the ability to switch an existing cell's type.

### Workaround

Extension-specific cell types (e.g. SQL) can be authored in `.verso` files directly or added programmatically via `NotebookService.AddCell("sql")`.

---

## VERSO-003: F# language kernel not supported

| | |
|---|---|
| **Status** | In Development |
| **Affected** | Verso |
| **Severity** | Low |

### Symptom

Cells with `language: "fsharp"` cannot be executed. The only built-in language kernel is C# (Roslyn). Attempting to run an F# cell falls through the `ExecutionPipeline` resolution chain and either executes against the default C# kernel (producing compilation errors) or fails with no matching kernel.

### Root cause

No `ILanguageKernel` implementation exists for F#. The kernel architecture supports it — adding F# would follow the same pattern as `CSharpKernel` or `SqlKernel` — but the implementation has not been built yet.

### Planned fix

An F# kernel extension implementing `ILanguageKernel` with `LanguageId: "fsharp"`.

### Workaround

None. F# code cannot be executed in Verso at this time.
