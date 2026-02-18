# Verso Notebook for VS Code

Interactive `.verso` notebooks in VS Code and Cursor, with C# and SQL execution, database connectivity, IntelliSense, and EF Core scaffolding.

## Setup

1. Install the extension (`.vsix` or from the marketplace).
2. Set `verso.hostPath` in VS Code settings to your built `Verso.Host.dll` (or leave blank to auto-detect from the workspace):

```json
{
  "verso.hostPath": "/path/to/tools/Verso/src/Verso.Host/bin/Debug/net8.0/Verso.Host.dll"
}
```

3. Open any `.verso` file to activate the extension.

## Editor Modes

The extension provides two ways to open `.verso` files:

### Native Notebook (default)

The default editor uses VS Code's built-in notebook API with native cell rendering, IntelliSense, and diagnostics. This is the standard experience when you open a `.verso` file.

### Blazor Editor (Open With...)

Right-click a `.verso` file and choose **Open With... > Verso Notebook (Experimental)** to open it in the full Blazor UI. This mode runs the same Razor components used by the standalone Blazor web application inside a VS Code webview via WebAssembly. It includes the full notebook toolbar, side panels (metadata, extensions, variables, settings), dashboard layout, and theme switching.

The Blazor editor communicates with the Verso engine through a postMessage/JSON-RPC bridge — the engine runs in the `Verso.Host` process, not in WASM.

## Features

### Notebook Editing

- Execute cells individually or run all with the toolbar
- C# and SQL language kernels
- Markdown cells with preview
- Restart kernel, clear outputs, switch layout and theme from the toolbar

### Sidebar

- **Extensions panel** -- view and enable/disable loaded Verso extensions (kernels, themes, formatters, etc.)
- **Variables panel** -- inspect in-scope variables with type and value preview; auto-refreshes after cell execution

### IntelliSense

- **Completions** -- C# completions via Roslyn; SQL keyword, table/column, and `@variable` completions from cached schema metadata. Triggered on `.` in C# cells.
- **Hover** -- type and documentation info for C# symbols; SQL keyword descriptions, table column lists, column data types, and `@variable` types/values.
- **Diagnostics** -- inline errors and warnings as you type (debounced). SQL diagnostics include missing connection and unresolved parameter warnings.

## SQL Database Support

Verso.Ado provides provider-agnostic SQL support through `System.Data.Common`. Load your ADO.NET provider with `#r "nuget:"`, then connect.

### Connecting

```
#r "nuget: Microsoft.Data.SqlClient"

#!sql-connect --name northwind --connection-string "Server=localhost;Database=Northwind;Trusted_Connection=true" --default
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `--name` | Yes | Unique name for this connection |
| `--connection-string` | Yes | ADO.NET connection string. Supports `$env:VAR_NAME` and `$secret:SecretName` |
| `--provider` | No | Provider invariant name (e.g., `Npgsql`). Auto-detected if omitted |
| `--default` | No | Set as the default connection for SQL cells |

Multiple named connections are supported. Credentials referenced with `$env:` or `$secret:` are resolved at runtime and never persisted to the `.verso` file.

Disconnect with:

```
#!sql-disconnect --name northwind
```

### Executing SQL

SQL cells execute against the default connection, or specify one with a directive:

```sql
--connection analytics --name salesData
SELECT Region, SUM(Revenue) AS TotalRevenue
FROM Sales
WHERE Revenue > @minRevenue
GROUP BY Region
```

| Directive | Description |
|-----------|-------------|
| `--connection <name>` | Target a named connection |
| `--name <variable>` | Store the result as a variable (default: `lastSqlResult`) |
| `--no-display` | Execute without rendering output |

Results render as paginated HTML tables (default 50 rows/page) with column type tooltips, NULL styling, and a row count footer. A safety limit of 10,000 rows caps database reads per result set.

### Variable Sharing

**SQL to C#** -- SQL results are published to the variable store as `DataTable`:

```csharp
var sales = Variables.Get<DataTable>("salesData");
var top = sales.AsEnumerable()
    .Where(r => r.Field<decimal>("TotalRevenue") > 1_000_000)
    .ToList();
```

**C# to SQL** -- C# variables are available as parameterized SQL `@parameters`:

```csharp
var minRevenue = 500_000m;
```

```sql
SELECT * FROM Sales WHERE Revenue > @minRevenue
```

Parameters are bound as `DbParameter` objects (not string interpolation), preventing SQL injection.

### Export

SQL result cells show **Export CSV** and **Export JSON** toolbar buttons that export the full result set (not just the current page) as a file download.

### Schema Inspection

```
#!sql-schema                         -- list all tables and views
#!sql-schema --table Products        -- show column definitions
#!sql-schema --refresh               -- rebuild the schema cache
```

Schema metadata is cached per connection (default 300s) and powers table/column completions and hover info.

### EF Core Scaffolding

Generate a `DbContext` and entity classes from a live database:

```
#r "nuget: Microsoft.EntityFrameworkCore"
#r "nuget: Microsoft.EntityFrameworkCore.SqlServer"

#!sql-scaffold --connection northwind
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `--connection` | Yes | Named connection to scaffold from |
| `--tables` | No | Comma-separated table filter |
| `--schema` | No | Limit to a specific schema |

The scaffolded `DbContext` is published to the variable store and available in C# cells:

```csharp
var ctx = Variables.Get<DbContext>("northwindContext");
var products = await ctx.Set<Product>()
    .Where(p => p.UnitPrice > 50)
    .OrderBy(p => p.ProductName)
    .ToListAsync();
```

## Importing Polyglot Notebooks

Opening a `.ipynb` file automatically converts Polyglot Notebooks SQL patterns to native Verso format:

| Polyglot pattern | Converted to |
|------------------|--------------|
| `#!connect mssql --kernel-name db "..."` | `#!sql-connect --name db --connection-string "..." --provider Microsoft.Data.SqlClient` |
| `#!connect postgresql ...` | `#!sql-connect ... --provider Npgsql` |
| `#!connect mysql ...` | `#!sql-connect ... --provider MySql.Data.MySqlClient` |
| `#!connect sqlite ...` | `#!sql-connect ... --provider Microsoft.Data.Sqlite` |
| `#!sql` / `#!<kernelName>` cells | SQL cell type with `--connection` directive |
| `--create-dbcontext` flag | Separate `#!sql-scaffold` cell |

Missing `#r "nuget:"` directives for detected providers are inserted automatically.

## Commands

| Command | Description |
|---------|-------------|
| Verso: Run All Cells | Execute every cell in order |
| Verso: Run Cell | Execute the current cell |
| Verso: Clear All Outputs | Clear all cell outputs |
| Verso: Restart Kernel | Restart the .NET runtime |
| Verso: Switch Layout | Choose between linear and dashboard layouts |
| Verso: Switch Theme | Apply a different notebook theme |

## Building from Source

```bash
# From the vscode/ directory:

# Install dependencies
npm install

# Build TypeScript only (native notebook mode)
npm run build

# Build TypeScript + Blazor WASM (includes Blazor editor)
npm run build:all

# Package the extension
npm run package
```

The `build:all` script runs `dotnet publish` on `Verso.Blazor.Wasm` and outputs the WASM files to `blazor-wasm/`, then bundles the TypeScript.
