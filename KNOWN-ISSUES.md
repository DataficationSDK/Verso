# Known Issues

> Tracked issues and workarounds for the Verso notebook engine.

| # | Issue | Status | Affected |
|---|-------|--------|----------|
| [VERSO-001](#verso-001-browse-file-picker-breaks-relative-imports-in-blazor) | Browse file picker breaks relative imports in Blazor | Fixed | Verso.Blazor |
| [VERSO-002](#verso-002-f-none-values-cannot-be-stored-in-the-variable-store) | F# `None` values cannot be stored in the variable store | By Design | Verso.FSharp |
| [VERSO-003](#verso-003-f-anonymous-records-not-recognized-by-data-formatter) | F# anonymous records not recognized by data formatter | Fixed | Verso.FSharp |
| [VERSO-004](#verso-004-f-compiler-settings-changes-require-kernel-restart) | F# compiler settings changes require kernel restart | By Design | Verso.FSharp |
| [VERSO-005](#verso-005-jupyter-f-import-share-uses-untyped-variable-binding) | Jupyter F# import `#!share` uses untyped variable binding | Open | Verso.FSharp |
| [VERSO-006](#verso-006-blazor-wasm-webview-fails-to-initialize-in-github-codespaces) | Blazor WASM webview fails to initialize in GitHub Codespaces | Mitigated | Verso.VSCode |
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

The browser's security model strips the full directory path from file picker selections, leaving only the filename. `OpenFromContentAsync` created a `Scaffold` without a `filePath`, so `ImportMagicCommand.ResolvePath` fell back to `Directory.GetCurrentDirectory()` (the Blazor project directory) instead of the notebook's actual directory.

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

The F# kernel's automatic variable publishing already handles this, excluding `None` bindings from the variable store during the post-execution diff.

---

## VERSO-003: F# anonymous records not recognized by data formatter

| | |
|---|---|
| **Status** | Fixed |
| **Affected** | Verso.FSharp |
| **Severity** | Low |
| **Fixed in** | No code change required, resolved by the compiler services version in use |

### Symptom (historical)

Anonymous record values (`{| Name = "Alice"; Age = 30 |}`) were reported as not rendering with the rich HTML table format, falling back to plain-text `ToString()` output.

### Root cause (historical)

The original diagnosis assumed anonymous records compile to plain anonymous types without the `CompilationMapping` attribute, which would make `FSharpType.IsRecord(type, null)` return `false` inside `FSharpDataFormatter.CanFormat`.

### Resolution

That assumption does not hold for the compiler services version currently referenced (`FSharp.Compiler.Service` 43.12.202). Anonymous record types do carry `CompilationMappingAttribute`, so `FSharpType.IsRecord` recognizes them and the existing `CanFormat` path already accepts them. No formatter change was needed.

Verified by executing `{| Name = "Alice"; Age = 30 |}` through the F# kernel and formatting the stored value: the generated type is `<>f__AnonymousType...` carrying `CompilerGeneratedAttribute`, `CompilationMappingAttribute` and `SerializableAttribute`, `FSharpType.IsRecord` returns `true`, and the output is the standard Field/Value table. Covered by `FormatAsync_KernelAnonymousRecord_RendersFieldTable`.

### Note

F# orders anonymous record fields alphabetically rather than in literal order, so the table above lists `Age` before `Name`. This is a language characteristic, not a formatter behavior.

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

After changing compiler settings, restart the F# kernel via `#!restart` or the toolbar restart button. The `publishPrivateBindings` and `maxCollectionDisplay` settings take effect immediately without a restart, as they are applied at execution time rather than during session creation.

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

The `JupyterFSharpPostProcessor` cannot determine the runtime type of shared variables at import time, since type information is only available during execution. The generated code uses `obj` as a safe fallback.

### Workaround

Add a type annotation or downcast after import:

```fsharp
let myVar = Variables.Get<obj>("myVar") :?> int
```

---

## VERSO-006: Blazor WASM webview fails to initialize in GitHub Codespaces

| | |
|---|---|
| **Status** | Mitigated |
| **Affected** | Verso.VSCode |
| **Severity** | High |
| **Fixed in** | `blazorEditorProvider.ts` (webview CSP and boot-resource remapping), `WebviewNavigationManager.cs` |

### Symptom (historical)

When opening a notebook in GitHub Codespaces (browser-based VS Code), the editor got stuck on the loading spinner and never rendered. The Blazor WASM runtime failed to initialize inside the webview, and no error was surfaced to the user.

### Root cause (historical)

At the time this issue was filed, the webview HTML had two defects that broke the Blazor boot sequence under the origins used by browser-based VS Code:

1. **WebAssembly instantiation was blocked**: the webview's Content Security Policy did not include the `wasm-unsafe-eval` directive required to compile and execute .NET WebAssembly modules.
2. **Framework fetches assumed the desktop URI scheme**: the `loadBootResource` remapping assumed `vscode-webview://` URIs, which do not exist in browser-based VS Code, so framework fetches failed.

Because the Blazor boot process did not surface these failures, `Blazor.start()` hung and the webview remained in its loading state indefinitely.

### Resolution

Later reworks of the webview host removed both causes, and the failure no longer reproduces in Chromium-based browsers:

- The webview CSP now includes `'wasm-unsafe-eval'`.
- `loadBootResource` remaps every framework fetch to an absolute URI computed from `webview.asWebviewUri()`, which is valid under any webview origin (the desktop scheme and the `https` origins used by browser-based VS Code alike).
- The default Blazor `NavigationManager` is replaced with a synthetic-base stub so .NET never parses the webview origin.
- Blazor startup failures now surface as an error message in the loading screen instead of hanging silently.

Verified working in the GitHub Codespaces browser editor with Chrome (July 2026): the notebook UI loads, the kernel host runs inside the codespace container, and cells execute normally. The first open in a fresh codespace is slower than desktop because the WASM runtime and assemblies (~16 MB) stream through the Codespaces connection; subsequent opens benefit from browser caching.

### Remaining limitation

Safari cannot load the notebook editor in the Codespaces browser client: the webview content never finishes loading and VS Code's own webview host page repeatedly logs `TypeError: null is not an object (evaluating 'target.contentDocument.body.classList')` from its focus-tracking loop. The stall occurs in VS Code's browser webview host and is not specific to Verso, which is also why this entry remains Mitigated rather than Fixed. Use a Chromium-based browser (Chrome or Edge) or desktop VS Code.

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

Two mitigations are in place.

A 512 KB cap (`ObjectTreeRenderer.MaxOutputSize`) stops further tree expansion once the rendered HTML exceeds that threshold. Values beyond the cap fall back to `.ToString()`, matching the behavior at the depth limit. This prevents the serialization crash while still providing tree view output for the portion of the graph that fits within the budget.

Framework types (`System.*` and `Microsoft.*`) are also rendered via `.ToString()` at depth 2 and deeper instead of having their property graphs expanded, which removes the `Type` to `Assembly` to `DefinedTypes` path entirely. Framework types at depth 0 and 1 still expand so immediate properties remain inspectable, and namespace matching is exact so user namespaces such as `Systematic` are unaffected.

Collections need a separate rule, because reflection metadata is reached through collection properties (`Type.CustomAttributes`, and in turn `CustomAttributeData.ConstructorArguments` and `NamedArguments`). Suppressing them by type does not work, since `List<string>` and `ReadOnlyCollection<CustomAttributeData>` both live in `System.*`, and element types are no better because `Dictionary<string, object>` yields `KeyValuePair`. The renderer instead tracks whether a value was reached by expanding a framework object, and only summarizes collections inside such a subtree. User collections expand at every depth regardless of nesting, and a reflection collection queried directly, such as the result of `typeof(T).GetMethods()`, still expands because it arrives as the root value rather than as framework internals.

Together these bound the cases that motivated this entry. An object holding a single `System.Type` property renders about 12 KB, and a bare `System.Type` about 34 KB, against roughly 600 KB and 640 KB before. `FormatAsync_TypeMember_ProducesBoundedOutput` and `FormatAsync_BareTypeValue_ProducesBoundedOutput` assert the size directly, since asserting the absence of a single member name proved too weak to catch fan-out that had simply moved to another property.

### Workaround

If you encounter this issue with a specific type, register a higher-priority custom formatter for that type to control its rendering directly:

```csharp
// In a notebook cell, before returning the value:
#!register-formatter MyNamespace.MyType text/html (value, context) => {
    return $"<pre>{value.SomeProperty}: {value.SomeOtherProperty}</pre>";
}
```

### Remaining limitation

Asking for a reflection collection directly still produces sizeable output, because such a value is the root rather than framework internals and the first two levels expand by design. Rendering five `MethodInfo` values returns roughly 170 KB, since the table carries one column per public member and each cell expands its own members one level further. This stays below the cap and is the intended behavior for an explicit query, but a large query such as the full method list of a common type will reach the cap and be truncated.

The cap itself overshoots slightly. It is tested when a value is about to be rendered, so a table already in progress keeps emitting markup after the budget is exhausted. Testing the budget before opening each new node would make the limit hold exactly.

### Planned improvement

Framework types with useful members but an unhelpful `ToString()` lose information at depth 2 and deeper. `Task<T>` is the clearest case: it renders as its raw type name where it previously showed `Result`, `Status` and `IsCompleted`. Types such as `System.Data.DataTable` and `Lazy<T>` are affected the same way, while types with meaningful `ToString()` output such as `Stopwatch`, `Uri` and `TimeSpan` read well. Narrowing the opacity rule from all of `System.*` and `Microsoft.*` to reflection namespaces specifically would preserve those members, at the cost of reintroducing fan-out for any other framework type with a wide property graph.
