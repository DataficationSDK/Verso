# Database Connectivity

Verso.Ado is the SQL extension for Verso notebooks. It provides provider-agnostic database connectivity through ADO.NET, letting you connect to any supported database, execute queries with paginated result tables, inspect schema, scaffold EF Core classes, and export results to CSV or JSON.

## Connecting to a Database

Use the `#!sql-connect` magic command in a code cell to establish a connection:

```
#!sql-connect --name mydb --connection-string "Server=localhost;Database=Northwind;User Id=sa;Password=secret"
```

### Parameters

| Parameter | Required | Description |
|-----------|:--------:|-------------|
| `--name` | Yes | A friendly name used to reference this connection |
| `--connection-string` | Yes | An ADO.NET connection string |
| `--provider` | No | The DbProviderFactory invariant name (auto-detected when omitted) |
| `--default` | No | Flag that sets this connection as the default |

The first connection you open is automatically set as the default, even without `--default`. Subsequent connections require the flag if you want them to become the new default.

On success, the output confirms the connection name, provider, database, and a redacted connection string (passwords replaced with `***`).

### Connection String Placeholders

Connection strings support dynamic token substitution to avoid hardcoding sensitive values:

| Token | Resolves to |
|-------|-------------|
| `$env:VAR_NAME` | The environment variable `VAR_NAME` |
| `$var:VarName` | A variable from the notebook's variable store |

Set a variable in a C# cell, then reference it in the connection string:

```csharp
var dbHost = "prod-sql-01.internal";
```

```
#!sql-connect --name prod --connection-string "Server=$var:dbHost;Database=Analytics;Integrated Security=true"
```

Environment variables work the same way:

```
#!sql-connect --name staging --connection-string "Server=$env:DB_SERVER;Database=$env:DB_NAME;User Id=$env:DB_USER;Password=$env:DB_PASS"
```

### Provider Auto-Detection

When you omit the `--provider` parameter, Verso.Ado infers the provider from keywords in the connection string:

| Connection string pattern | Detected provider |
|---------------------------|-------------------|
| `Data Source=:memory:` or `.db` file path | Microsoft.Data.Sqlite |
| `Server=` or `Data Source=` | Microsoft.Data.SqlClient |
| `Host=` or `Port=5432` or `SslMode=` | Npgsql (PostgreSQL) |
| `Server=localhost;Port=3306` or `Uid=` | MySql.Data.MySqlClient |

You can always specify the provider explicitly when auto-detection does not fit your setup:

```
#!sql-connect --name mydb --connection-string "..." --provider Npgsql
```

### Supported Providers

Verso.Ado works with any ADO.NET provider. The following are recognized by name for direct resolution:

| Invariant name | Provider |
|----------------|----------|
| `Microsoft.Data.SqlClient` | SQL Server |
| `Microsoft.Data.Sqlite` | SQLite |
| `Npgsql` | PostgreSQL |
| `MySql.Data.MySqlClient` | MySQL (Oracle connector) |
| `MySqlConnector` | MySQL (MySqlConnector) |
| `Oracle.ManagedDataAccess.Client` | Oracle |

Provider assemblies are not bundled with Verso.Ado. Install the NuGet package for your provider before connecting:

```
#!nuget Microsoft.Data.SqlClient
```

```
#!nuget Npgsql
```

### Managing Connections

Disconnect a named connection:

```
#!sql-disconnect --name mydb
```

Or disconnect the current default:

```
#!sql-disconnect
```

When the default connection is disconnected, the next remaining connection is promoted to default.

### Multiple Connections

You can maintain several connections simultaneously and target them per cell:

```
#!sql-connect --name analytics --connection-string "Host=pg-analytics;Database=reports;..." --default
#!sql-connect --name warehouse --connection-string "Server=sql-warehouse;Database=dw;..."
```

Then specify which connection a SQL cell should use with the `--connection` directive (see [SQL Directives](#sql-directives) below).

## Writing SQL Queries

Once connected, any SQL cell runs against the default connection:

```sql
SELECT TOP 10 CustomerName, OrderCount
FROM Customers
ORDER BY OrderCount DESC
```

Results render as paginated HTML tables directly in the cell output, with column headers, type-aware formatting, and null value indicators.

### SQL Directives

SQL cells support directives on the first line to control execution behavior. Write them as a comment line starting with `--`:

```sql
-- --connection warehouse --name warehouseResults --page-size 500
SELECT * FROM FactSales WHERE Year = 2025
```

| Directive | Default | Description |
|-----------|---------|-------------|
| `--connection` | Default connection | Target a specific named connection |
| `--name` | `lastSqlResult` | Variable name for the result in the variable store |
| `--page-size` | 10,000 | Maximum rows to fetch from the database |
| `--no-display` | Off | Suppress output (the result is still stored in the variable store) |

The first line is only treated as directives if it starts with `--` and contains at least one recognized directive key.

### Variable Binding

SQL cells can reference notebook variables as parameters using `@variableName` syntax:

```csharp
var region = "US-East";
var minAmount = 100.0m;
```

```sql
SELECT * FROM Orders
WHERE Region = @region AND TotalAmount > @minAmount
```

Variable names are matched case-insensitively from the notebook's variable store. The kernel maps .NET types to `DbType` values automatically. If a referenced variable does not exist, IntelliSense shows a diagnostic warning.

### Multi-Statement Execution

Semicolons separate individual SQL statements within a single cell. Each statement executes sequentially, and result sets are displayed in order:

```sql
SELECT COUNT(*) AS TotalOrders FROM Orders;
SELECT TOP 5 * FROM Orders ORDER BY OrderDate DESC;
```

For SQL Server, `GO` batch separators are also supported. `GO` must appear at the start of a line:

```sql
CREATE TABLE #TempResults (Id INT, Name NVARCHAR(100))
GO
INSERT INTO #TempResults SELECT Id, Name FROM Products WHERE Active = 1
GO
SELECT * FROM #TempResults
```

Non-query statements (INSERT, UPDATE, DELETE) display the number of affected rows and elapsed time.

## Using Results in Other Languages

Query results are automatically stored in the notebook's variable store, making them available to C#, F#, and other language cells.

By default, the last query result is stored as `lastSqlResult` (a `System.Data.DataTable`). Use the `--name` directive to choose a different variable name:

```sql
-- --name customers
SELECT * FROM Customers
```

```csharp
// Access the DataTable in C#
var dt = customers;
Console.WriteLine($"Rows: {dt.Rows.Count}");

foreach (DataRow row in dt.Rows)
{
    Console.WriteLine($"{row["CustomerName"]}: {row["OrderCount"]}");
}
```

A typed `SqlResultSet` is also stored under `{name}__resultset` with structured column metadata:

```csharp
var rs = lastSqlResult__resultset;
foreach (var col in rs.Columns)
{
    Console.WriteLine($"{col.Name} ({col.DataTypeName}, nullable: {col.AllowsNull})");
}
```

## CI/CD Pipelines

Notebooks that connect to databases can run headlessly in CI/CD pipelines using `verso run`. Notebook parameters provide a clean way to pass environment-specific values like connection strings, server names, or query filters without modifying the notebook itself.

### Notebook Setup

Define parameters in your notebook for values that change between environments:

| Parameter | Type | Description |
|-----------|------|-------------|
| `dbServer` | string | Database server hostname |
| `dbName` | string | Database name |
| `reportDate` | date | Date to run the report for |

Then reference those parameters in your connection and queries:

```
#!sql-connect --name reporting --connection-string "Server=$var:dbServer;Database=$var:dbName;Integrated Security=true"
```

```sql
SELECT * FROM DailySummary WHERE ReportDate = @reportDate
```

Parameters are injected into the variable store before any cells execute, so they are available to both `$var:` tokens in connection strings and `@variable` bindings in SQL queries.

### GitHub Actions

Use `--param` flags on `verso run` to pass values from GitHub Actions secrets and variables:

```yaml
jobs:
  report:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - run: dotnet tool install -g Verso.Cli

      - name: Run report notebook
        run: |
          verso run reports/daily-summary.verso \
            --param dbServer=${{ secrets.DB_SERVER }} \
            --param dbName=${{ vars.DB_NAME }} \
            --param reportDate=$(date +%Y-%m-%d) \
            --output json > results.json
```

For connection strings that contain special characters, wrap the value in quotes:

```yaml
      - name: Run with full connection string
        run: |
          verso run pipeline.verso \
            --param connectionString="${{ secrets.DB_CONNECTION_STRING }}"
```

### Azure DevOps

The same approach works with Azure DevOps pipeline variables and variable groups:

```yaml
trigger:
  - main

pool:
  vmImage: 'ubuntu-latest'

variables:
  - group: database-credentials

steps:
  - task: UseDotNet@2
    inputs:
      version: '10.0.x'

  - script: dotnet tool install -g Verso.Cli
    displayName: 'Install Verso CLI'

  - script: |
      verso run reports/daily-summary.verso \
        --param dbServer=$(DB_SERVER) \
        --param dbName=$(DB_NAME) \
        --param reportDate=$(Build.BuildId)
    displayName: 'Run report notebook'
    env:
      DB_PASSWORD: $(DB_PASSWORD)
```

When a secret should not appear in the `--param` value directly, pass it as an environment variable and reference it with `$env:` in the notebook's connection string:

```
#!sql-connect --name prod --connection-string "Server=$var:dbServer;Database=$var:dbName;User Id=svc_account;Password=$env:DB_PASSWORD"
```

This keeps the secret out of command-line arguments and process listings.

### Required Parameters

Mark parameters as required in the notebook definition to catch missing values early. If `verso run` is invoked without a required parameter that has no default, it exits with a non-zero exit code and lists the missing parameters, failing the pipeline step immediately rather than running with incomplete configuration.

## Schema Inspection

Use `#!sql-schema` to explore your database structure without writing queries:

```
#!sql-schema
```

This lists all tables and views with their name, schema, and type.

### Inspecting a Specific Table

```
#!sql-schema --table Orders
```

This displays column-level detail: name, data type, nullability, default value, and whether the column is a primary key.

### Parameters

| Parameter | Description |
|-----------|-------------|
| `--connection` | Inspect a specific named connection (defaults to the default connection) |
| `--table` | Show column detail for a named table |
| `--refresh` | Force a cache refresh before displaying results |

Schema data is cached for 5 minutes per connection. The cache is shared with IntelliSense, so completions reflect the same schema you see in `#!sql-schema`.

## EF Core Scaffolding

The `#!sql-scaffold` command generates Entity Framework Core entity classes and a `DbContext` directly in the notebook, compiled and ready to use:

```
#!sql-scaffold --connection mydb
```

### Parameters

| Parameter | Required | Description |
|-----------|:--------:|-------------|
| `--connection` | Yes | The named connection to scaffold from |
| `--tables` | No | Comma-separated list of tables to include (defaults to all) |
| `--schema` | No | Filter tables by schema name (e.g., `dbo`) |

### What Gets Generated

For a connection named `Northwind` with tables `Customers` and `Orders`:

- Entity classes `Customer` and `Order` with `[Table]`, `[Key]`, `[Column]`, and `[ForeignKey]` attributes where needed
- Navigation properties for foreign key relationships
- A `NorthwindContext` class extending `DbContext` with `DbSet<T>` properties for each table
- A context variable named `northwindContext` (camelCase) in the variable store

The generated code is compiled and executed by the C# kernel immediately. A collapsible code block in the output shows the full generated source.

### Using the Scaffolded Context

```csharp
var topCustomers = northwindContext.Customers
    .OrderByDescending(c => c.OrderCount)
    .Take(10)
    .ToList();
```

### NuGet Prerequisites

EF Core packages are not bundled with Verso.Ado. Install the provider-specific EF Core package before scaffolding:

| Database | Required packages |
|----------|-------------------|
| SQL Server | `Microsoft.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore.SqlServer` |
| PostgreSQL | `Microsoft.EntityFrameworkCore` + `Npgsql.EntityFrameworkCore.PostgreSQL` |
| SQLite | `Microsoft.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore.Sqlite` |
| MySQL | `Microsoft.EntityFrameworkCore` + `Pomelo.EntityFrameworkCore.MySql` |

```
#!nuget Microsoft.EntityFrameworkCore
#!nuget Microsoft.EntityFrameworkCore.SqlServer
```

## Exporting Results

SQL cells with results show CSV and JSON export buttons in the cell toolbar. These download the full result set (not just the visible page) as a file.

The export actions appear only on cells that have a stored query result.

## IntelliSense

The SQL kernel provides completions, diagnostics, and hover information:

- **Completions** include table names, column names (scoped to tables in the query), SQL keywords, and `@variable` references from the notebook variable store
- **Diagnostics** warn when no connection is found or when `@parameter` references have no matching variable
- **Hover** on a table name shows its columns, on a column shows its type and nullability, on `@variable` shows its current type and value, and on SQL keywords shows descriptions

IntelliSense uses the same schema cache as `#!sql-schema`, so schema is fetched once and reused.

## Importing from Polyglot Notebooks

When you open a `.ipynb` or `.dib` file that contains Polyglot Notebooks SQL patterns, Verso automatically converts them:

| Polyglot pattern | Verso equivalent |
|------------------|------------------|
| `#!connect mssql --kernel-name mydb "..."` | `#!sql-connect --name mydb --connection-string "..." --provider Microsoft.Data.SqlClient` |
| `#!connect postgresql --kernel-name pg "..."` | `#!sql-connect --name pg --connection-string "..." --provider Npgsql` |
| `#!connect mysql --kernel-name my "..."` | `#!sql-connect --name my --connection-string "..." --provider MySql.Data.MySqlClient` |
| `#!connect sqlite --kernel-name lite "..."` | `#!sql-connect --name lite --connection-string "..." --provider Microsoft.Data.Sqlite` |
| `#!sql` cell prefix | SQL cell type |
| `#!kernelName` (matching a connection name) | SQL cell with `--connection kernelName` directive |

If the original notebook used `--create-dbcontext`, Verso inserts a `#!sql-scaffold` cell for that connection. Missing NuGet package references are added automatically.

## Provider-Specific Notes

### SQL Server

- Provider: `Microsoft.Data.SqlClient`
- `GO` batch separators are supported in multi-statement cells
- Integrated Security (Windows Authentication) works when running locally
- Schema inspection uses `INFORMATION_SCHEMA`

### PostgreSQL

- Provider: `Npgsql`
- Connection strings typically use `Host=`, `Port=`, `Username=`, `Password=`, `Database=`
- Schema inspection uses `INFORMATION_SCHEMA`
- SSL connections supported via `SslMode=` in the connection string

### SQLite

- Provider: `Microsoft.Data.Sqlite`
- Supports both file-based (`Data Source=mydb.db`) and in-memory (`Data Source=:memory:`) databases
- Schema inspection uses `sqlite_master` and `PRAGMA` commands instead of `INFORMATION_SCHEMA`
- Foreign key queries use `PRAGMA foreign_key_list`

### MySQL

- Providers: `MySql.Data.MySqlClient` (Oracle) or `MySqlConnector`
- Schema inspection uses `INFORMATION_SCHEMA`
- EF Core scaffolding uses the Pomelo provider (`Pomelo.EntityFrameworkCore.MySql`)

### Oracle

- Provider: `Oracle.ManagedDataAccess.Client`
- Recognized for provider resolution but has no special-case handling in schema inspection (uses `INFORMATION_SCHEMA`)

### Firebird

- Detected by connection type name (`FbConnection`)
- Schema inspection uses Firebird-specific system tables (`RDB$RELATIONS`, `RDB$RELATION_FIELDS`)
