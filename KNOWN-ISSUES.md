# Known Issues

> Tracked issues and workarounds for the Verso notebook engine.

| # | Issue | Status | Affected |
|---|-------|--------|----------|
| [VERSO-001](#verso-001-browse-file-picker-breaks-relative-imports-in-blazor) | Browse file picker breaks relative imports in Blazor | Fixed | Verso.Blazor |
| [VERSO-002](#verso-002-f-none-values-cannot-be-stored-in-the-variable-store) | F# `None` values cannot be stored in the variable store | By Design | Verso.FSharp |
| [VERSO-003](#verso-003-f-anonymous-records-not-recognized-by-data-formatter) | F# anonymous records not recognized by data formatter | Open | Verso.FSharp |
| [VERSO-004](#verso-004-f-compiler-settings-changes-require-kernel-restart) | F# compiler settings changes require kernel restart | By Design | Verso.FSharp |
| [VERSO-005](#verso-005-jupyter-f-import-share-uses-untyped-variable-binding) | Jupyter F# import `#!share` uses untyped variable binding | Open | Verso.FSharp |
| [VERSO-006](#verso-006-blazor-wasm-webview-fails-to-initialize-in-github-codespaces) | Blazor WASM webview fails to initialize in GitHub Codespaces | Open | Verso.VSCode |
| [VERSO-007](#verso-007-object-tree-view-can-produce-oversized-output-for-complex-framework-types) | Object tree view can produce oversized output for complex framework types | Mitigated | Verso |

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

---

## VERSO-006: Blazor WASM webview fails to initialize in GitHub Codespaces

| | |
|---|---|
| **Status** | Open |
| **Affected** | Verso.VSCode |
| **Severity** | High |

### Symptom

When opening a notebook in GitHub Codespaces (browser-based VS Code), the editor gets stuck on the loading spinner and never renders. The Blazor WASM runtime fails to initialize inside the webview, and no error is surfaced to the user.

### Root cause

The VS Code extension hosts Blazor WASM as static files inside a custom editor webview. On desktop VS Code, `webview.asWebviewUri()` produces `vscode-webview://` URIs, and the `loadBootResource` callback in `Blazor.start()` remaps `_framework/` assembly fetches to these URIs successfully.

In GitHub Codespaces, webviews run as nested iframes under a different origin with a more restrictive security policy. This breaks the Blazor WASM boot sequence in at least two ways:

1. **WebAssembly instantiation may be blocked** — the nested iframe's Content Security Policy may not include the `wasm-unsafe-eval` directive required to compile and execute .NET WebAssembly modules.
2. **Framework assembly fetches fail** — the `loadBootResource` URL remapping assumes the `vscode-webview://` URI scheme. In browser-based VS Code the scheme and origin differ, causing the `.dll` and `.wasm` fetches to silently fail or be blocked by CORS.

Because the Blazor boot process does not surface these failures, `Blazor.start()` hangs and the webview remains in its initial loading state indefinitely.

### Workaround

Use the desktop version of VS Code (local or via Remote-SSH) instead of the browser-based Codespaces editor. The Blazor WASM webview initializes correctly in all desktop VS Code environments.

### Planned improvement

Surface an error message to the user when Blazor WASM initialization fails or times out, rather than showing an indefinite loading spinner.

---

## VERSO-007: Object tree view can produce oversized output for complex framework types

| | |
|---|---|
| **Status** | Mitigated |
| **Affected** | Verso |
| **Severity** | Medium |

### Symptom

Returning a value whose type has deep or wide object graphs (such as `Microsoft.Data.Analysis.DataFrame`) can produce extremely large cell output (hundreds of megabytes), causing a `System.ArgumentException: The JSON value of length N is too large` error during notebook auto-save serialization.

### Root cause

The `ObjectFormatter` and `CollectionFormatter` use recursive tree view rendering (`<details>`/`<summary>`) to let users expand nested objects. When the returned value exposes framework infrastructure types through its public properties, the combinatorial fan-out across 6 recursion levels generates massive HTML output. For example, `DataFrameColumn.DataType` returns a `System.Type` with ~40+ public properties including `Assembly`, which in turn exposes `DefinedTypes` containing potentially thousands of types, each with their own property graphs.

### Mitigation

A 512 KB hard cap (`ObjectTreeRenderer.MaxOutputSize`) stops further tree expansion once the rendered HTML exceeds that threshold. Values beyond the cap fall back to `.ToString()`, matching the behavior at the depth limit. This prevents the serialization crash while still providing tree view output for the portion of the graph that fits within the budget.

### Workaround

If you encounter this issue with a specific type, register a higher-priority custom formatter for that type to control its rendering directly:

```csharp
// In a notebook cell, before returning the value:
#!register-formatter MyNamespace.MyType text/html (value, context) => {
    return $"<pre>{value.SomeProperty}: {value.SomeOtherProperty}</pre>";
}
```

### Planned improvement

Consider treating types from runtime assemblies (`System.*`, `System.Reflection.*`) as opaque beyond depth 1, rendering them via `.ToString()` rather than expanding their full property graphs. This would reduce wasted output budget on framework internals and preserve more of it for user-defined types.
