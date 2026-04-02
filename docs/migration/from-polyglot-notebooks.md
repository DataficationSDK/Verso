# Migrating from Polyglot Notebooks

This guide covers migrating from Polyglot Notebooks (and .NET Interactive) to Verso. If you used Polyglot Notebooks for interactive C#, F#, PowerShell, SQL, or multi-language notebooks, Verso provides a familiar experience with the same core workflow: write code in cells, execute them, and share state across languages.

## Automatic Import

Verso can open both `.dib` and `.ipynb` files created by Polyglot Notebooks. No manual conversion is required for most notebooks.

### Opening in VS Code

Open any `.dib` or `.ipynb` file in VS Code with the Verso extension installed. Use **Open With...** and select Verso if the file is associated with another editor. Verso converts the content automatically and displays it as a native Verso notebook.

### Opening in the Browser

Launch Verso in the browser with `verso serve`, then open the file through the UI. The import runs the same conversion pipeline.

### Converting via the CLI

To convert a file to the native `.verso` format permanently:

```bash
verso convert notebook.dib --to verso
verso convert notebook.ipynb --to verso
```

The original file is not modified. The converted file is written alongside it with a `.verso` extension, or to a path you specify with `--output`. Use `--strip-outputs` to remove all cell outputs during conversion.

Saving an imported notebook in Verso always writes to a new `.verso` file, preserving the original.

## What Gets Converted

### Cell Types

Verso recognizes all standard Polyglot Notebook language directives and maps them to native cell types:

| Polyglot Directive | Verso Cell |
|-------------------|------------|
| `#!csharp` or `#!c#` | C# code cell |
| `#!fsharp` or `#!f#` | F# code cell |
| `#!pwsh` or `#!powershell` | PowerShell code cell |
| `#!javascript` or `#!js` | JavaScript code cell |
| `#!sql` | SQL code cell |
| `#!html` | HTML cell |
| `#!mermaid` | Mermaid cell |
| `#!markdown` | Markdown cell |
| `#!value` | Code cell (value language) |

For `.dib` files, these directives act as cell boundaries. Each directive starts a new cell with the corresponding language. Content before the first directive uses the notebook's default kernel (from the `#!meta` block), falling back to C#.

For `.ipynb` files, the kernel language is extracted from `metadata.kernelspec.language` or `metadata.language_info.name`. All code cells are assigned that language. The `dotnet_interactive.language` cell metadata is used by the F# post-processor to correctly identify F# cells in multi-language Jupyter notebooks.

### Database Connections

Polyglot Notebooks' `#!connect` command is converted to Verso's `#!sql-connect` syntax:

| Polyglot | Verso |
|----------|-------|
| `#!connect mssql --kernel-name mydb "Server=...;Database=..."` | `#!sql-connect --name mydb --connection-string "Server=...;Database=..." --provider Microsoft.Data.SqlClient` |
| `#!connect postgresql --kernel-name pg "Host=..."` | `#!sql-connect --name pg --connection-string "Host=..." --provider Npgsql` |
| `#!connect mysql --kernel-name my "Server=..."` | `#!sql-connect --name my --connection-string "Server=..." --provider MySql.Data.MySqlClient` |
| `#!connect sqlite --kernel-name lite "Data Source=..."` | `#!sql-connect --name lite --connection-string "Data Source=..." --provider Microsoft.Data.Sqlite` |

If the original `#!connect` included `--create-dbcontext`, Verso inserts an additional `#!sql-scaffold` cell for that connection.

A NuGet package reference cell (`#r "nuget: ..."`) is inserted before the connection cell if the required provider package is not already referenced in the notebook.

After conversion, SQL cells that targeted a named kernel (e.g., `#!mydb` followed by a query) become SQL cells with a `--connection mydb` directive prepended to the source.

### Variable Sharing

Polyglot Notebooks used `#!share` and `#!set` for cross-kernel variable sharing. Verso handles these differently depending on the source language.

**F# notebooks:** The F# post-processor converts these automatically:

| Polyglot | Verso (generated F# code) |
|----------|---------------------------|
| `#!set --name myVar --value @fsharp:42` | `Variables.Set("myVar", 42)` |
| `#!share --from csharp myData` | `let myData = Variables.Get<obj>("myData")` |

The `#!share` conversion uses `obj` as the type and adds a comment noting that you should add a type annotation. After import, update these to the correct types:

```fsharp
let myData = Variables.Get<DataTable>("myData")
```

**Other languages:** `#!share` and `#!set` with non-F# value prefixes (e.g., `@csharp:`) are not automatically converted. They pass through as literal source text and need manual migration. See [Manual Adjustments](#manual-adjustments) below.

### Magic Commands

| Polyglot Command | Verso Equivalent | Conversion |
|-----------------|------------------|------------|
| `#r "nuget: Package"` | `#r "nuget: Package"` | Same syntax, no change needed |
| `#!time` | `#!time` | Same syntax, works as-is |
| `#!connect` | `#!sql-connect` | Automatic (see above) |
| `#!sql` | SQL cell type | Automatic |
| `#!fsharp` / `#!csharp` / etc. | Cell language | Automatic |
| `#!share` | Variable store API | Automatic for F# only |
| `#!set` | Variable store API | Automatic for F# only |
| `#!lsmagic` | `#!about` | Manual replacement |
| `#!who` / `#!whos` | Variable Explorer panel | Use the UI sidebar instead |

### Kernel Metadata

For `.dib` files, the `#!meta` JSON block at the top of the file is parsed. The `kernelInfo.defaultKernelName` property sets the notebook's default kernel.

For `.ipynb` files, the default kernel is extracted from:
1. `metadata.kernelspec.language` (checked first)
2. `metadata.language_info.name` (fallback)

Kernel names containing `fsharp` (e.g., `.net-fsharp`) are normalized to `fsharp`. `C#` becomes `csharp`, `F#` becomes `fsharp`, and all other names are lowercased.

## Manual Adjustments

Some patterns require manual changes after import.

### Variable Sharing from Non-F# Kernels

If your notebook used `#!share` or `#!set` with C#, PowerShell, or other kernels, replace them with direct variable store calls:

**Polyglot (C#):**
```csharp
// In a C# cell, sharing to other kernels
#!set --name greeting --value @csharp:myGreeting
```

**Verso (C#):**
```csharp
// Variables are automatically shared. Any variable assigned in a C# cell
// is available in the variable store. No explicit sharing needed.
var greeting = "Hello from C#";
// Other kernels can access this via the variable store
```

In Verso, all kernels share a single variable store. Variables set in any cell are immediately available to all other cells. There is no need for explicit `#!share` or `#!set` commands in most cases.

### NuGet Package Sources

Polyglot's `#i "nuget: <source-url>"` syntax for custom NuGet sources works the same way in Verso. No changes needed.

### Extension Loading

Polyglot's `#!import` for loading additional scripts has a Verso equivalent:

```
#!import path/to/notebook.verso --param name=value
```

This executes all code cells from the imported notebook in the current session, with optional parameter overrides.

### Unsupported Polyglot Features

| Feature | Status in Verso |
|---------|-----------------|
| `#!who` / `#!whos` | Use the Variable Explorer sidebar panel instead |
| `#!lsmagic` | Use `#!about` for extension and command info |
| KQL (Kusto) kernel | Not built-in; can be added as a third-party extension |
| R kernel | Not built-in; can be added as a third-party extension |
| Kernel-specific `#!share` | Use the shared variable store directly |

## Workflow Differences

### Variable Sharing

This is the most significant conceptual change. In Polyglot Notebooks, each kernel had its own isolated variable space, and `#!share` explicitly moved values between them. In Verso, all kernels share a single `VariableStore`. Any variable set in any cell is immediately available to every other cell, regardless of language.

This means you can remove most `#!share` and `#!set` commands. A C# cell that assigns `var data = LoadData()` makes `data` available to F#, Python, SQL, and other cells without any explicit sharing step.

### SQL Connectivity

Polyglot used `#!connect` with kernel names that became new cells addressable via `#!kernelName`. Verso uses named connections with `#!sql-connect` and the `--connection` directive in SQL cells. The result is the same, but the syntax is different. See the [database connectivity guide](../guides/database-connectivity.md) for full details.

### NuGet Packages

NuGet package references work the same way:

```csharp
#r "nuget: Newtonsoft.Json"
#r "nuget: Newtonsoft.Json/13.0.3"  // with version
```

### Layouts

Polyglot Notebooks had a fixed linear layout. Verso adds a dashboard layout option where cells can be arranged in a 12-column grid. Your imported notebooks start in the standard linear notebook layout.
