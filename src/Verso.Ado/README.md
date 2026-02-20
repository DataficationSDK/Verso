# Verso.Ado

SQL database connectivity extension for [Verso](https://github.com/DataficationSDK/Verso) notebooks.

## Overview

Provider-agnostic SQL support built on the public Verso extension interfaces. Connect to any ADO.NET database, execute SQL with paginated result tables, share variables between SQL and C# cells, inspect schema metadata, and scaffold EF Core DbContext classes at runtime.

### Features

- **Connection management** via `#!sql-connect` and `#!sql-disconnect` with named connections and provider auto-discovery
- **SQL kernel** with execution, `@parameter` binding from C# variables, `GO` batch separators, and row limiting
- **IntelliSense** for SQL keywords, table/column names, and `@variable` suggestions
- **Paginated HTML result tables** with column type tooltips, NULL styling, and truncation warnings
- **CSV and JSON export** toolbar actions
- **Schema inspection** via `#!sql-schema`
- **EF Core scaffolding** via `#!sql-scaffold`
- **Polyglot Notebooks migration** — automatic conversion of `#!connect` and `#!sql` patterns during `.ipynb` import

## Installation

```shell
dotnet add package Verso.Ado
```

This package depends only on [Verso.Abstractions](https://www.nuget.org/packages/Verso.Abstractions) and `System.Data.Common`. Supply your own ADO.NET provider (e.g. `Microsoft.Data.SqlClient`, `Npgsql`) via `#r "nuget:"` in your notebook.

## Quick Start

```
#!sql-connect "Server=localhost;Database=Northwind" --name db --provider SqlClient
```

```sql
SELECT TOP 10 * FROM Customers WHERE Country = @country
```
