# Known Issues

> Tracked issues and workarounds for the Verso notebook engine.

| # | Issue | Status | Affected |
|---|-------|--------|----------|
| [VERSO-001](#verso-001-browse-file-picker-breaks-relative-imports-in-blazor) | Browse file picker breaks relative imports in Blazor | Fixed | Verso.Blazor |
| [VERSO-002](#verso-002-f-none-values-cannot-be-stored-in-the-variable-store) | F# `None` values cannot be stored in the variable store | By Design | Verso.FSharp |
| [VERSO-003](#verso-003-f-anonymous-records-not-recognized-by-data-formatter) | F# anonymous records not recognized by data formatter | Open | Verso.FSharp |
| [VERSO-004](#verso-004-f-compiler-settings-changes-require-kernel-restart) | F# compiler settings changes require kernel restart | By Design | Verso.FSharp |
| [VERSO-005](#verso-005-jupyter-f-import-share-uses-untyped-variable-binding) | Jupyter F# import `#!share` uses untyped variable binding | Open | Verso.FSharp |

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

## VERSO-002: F# `None` values cannot be stored in the variable store

| | |
|---|---|
| **Status** | By Design |
| **Affected** | Verso.FSharp |
| **Severity** | Low |

### Symptom

Calling `Variables.Set("name", myOption)` where `myOption` is `None` throws `System.ArgumentNullException: Value cannot be null. (Parameter 'value')`.

### Root cause

F# `Option<'T>.None` is represented as `null` in .NET interop. `VariableStore.Set` requires non-null values by design (`ArgumentNullException.ThrowIfNull(value)`). This is a fundamental characteristic of how F# options map to the CLR.

### Workaround

Guard `Variables.Set` calls with a match expression:

```fsharp
match myOption with
| Some value -> Variables.Set("name", value)
| None -> ()  // None cannot be stored
```

The F# kernel's automatic variable publishing already handles this — `None` bindings are excluded from the variable store during the post-execution diff.

---

## VERSO-003: F# anonymous records not recognized by data formatter

| | |
|---|---|
| **Status** | Open |
| **Affected** | Verso.FSharp |
| **Severity** | Low |

### Symptom

Anonymous record values (`{| Name = "Alice"; Age = 30 |}`) are not rendered with the rich HTML table format. They fall back to plain-text `ToString()` output.

### Root cause

The `FSharpDataFormatter.CanFormat` method detects regular F# records via `FSharpType.IsRecord(type, null)`, which relies on the `CompilationMapping` attribute. F# anonymous records compile to anonymous types without this attribute, so they are not recognized as F# types.

### Planned fix

Add anonymous record detection to `CanFormat` by checking for types whose name starts with `<>f__AnonymousType` or by inspecting for the `CompilationMappingAttribute` with `SourceConstructFlags.RecordType` on individual properties.

### Workaround

Use named record types for rich formatting, or call `printfn "%A" value` for structured text output of anonymous records.

---

## VERSO-004: F# compiler settings changes require kernel restart

| | |
|---|---|
| **Status** | By Design |
| **Affected** | Verso.FSharp |
| **Severity** | Low |

### Symptom

Changing `warningLevel` or `langVersion` via the settings panel takes effect only after restarting the F# kernel. The current FSI session continues using the values it was initialized with.

### Root cause

The `FsiEvaluationSession` is created once during `InitializeAsync()` with the configured compiler arguments. FSI does not support changing compiler flags on a running session. The settings are stored on the `FSharpKernelOptions` record and used when the next session is created.

### Workaround

After changing compiler settings, restart the F# kernel via `#!restart` or the toolbar restart button. The `publishPrivateBindings` and `maxCollectionDisplay` settings take effect immediately without a restart.

---

## VERSO-005: Jupyter F# import `#!share` uses untyped variable binding

| | |
|---|---|
| **Status** | Open |
| **Affected** | Verso.FSharp |
| **Severity** | Low |

### Symptom

When importing a Jupyter notebook that uses `#!share --from csharp myVar`, the converted F# code binds the variable as `obj`:

```fsharp
let myVar = Variables.Get<obj>("myVar") // TODO: add type annotation (shared from csharp)
```

The user must manually add a type annotation or downcast to use the variable with its actual type.

### Root cause

The `JupyterFSharpPostProcessor` cannot determine the runtime type of shared variables at import time — type information is only available during execution. The generated code uses `obj` as a safe fallback.

### Workaround

Add a type annotation or downcast after import:

```fsharp
let myVar = Variables.Get<obj>("myVar") :?> int
```
