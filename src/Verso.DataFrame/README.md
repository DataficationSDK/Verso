# Verso.DataFrame

Rich table formatting for `Microsoft.Data.Analysis.DataFrame` values in
[Verso](https://github.com/DataficationSDK/Verso) notebooks.

## Features

- Automatic formatting of implicit PowerShell and .NET DataFrame results
- Column names and data-type annotations
- Theme-aware, scrollable HTML tables with sticky headers
- HTML encoding and explicit null rendering
- Bounded previews of the first 100 rows with total row counts
- No runtime dependency on `Microsoft.Data.Analysis`, avoiding assembly identity conflicts with language kernels and PowerShell modules

## Usage

Install or load the `Verso.DataFrame` extension, then return a DataFrame from a cell:

```powershell
Import-Module DataFrame

$penguins = Import-DataFrame ./data/penguins.csv
$penguins
```

The extension recognizes the runtime type and renders the DataFrame as a table. Explicit display
continues to work as well:

```powershell
Display $penguins
```

The package references only `Verso.Abstractions`. The DataFrame implementation remains owned by
the PowerShell module or language runtime that creates it.
