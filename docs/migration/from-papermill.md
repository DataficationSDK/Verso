# Migrating from Papermill

This guide covers migrating parameterized notebook workflows from [Papermill](https://papermill.readthedocs.io/) to Verso. Papermill is a Python tool for parameterizing and executing Jupyter notebooks, commonly used to run notebooks in CI/CD pipelines with different inputs. Verso provides a built-in parameter system and a headless CLI runner that serve the same purpose, with added support for typed parameters, required validation, multiple languages, and interactive prompting.

## Concept Mapping

| Papermill | Verso |
|-----------|-------|
| Tagged `parameters` cell with Python defaults | `metadata.parameters` dictionary with typed definitions |
| `papermill.execute_notebook()` | `verso run notebook.verso` |
| `-p name value` | `--param name=value` |
| `.ipynb` in, `.ipynb` out | `.verso` native format (imports `.ipynb`) |
| Python only | C#, F#, Python, JavaScript, TypeScript, PowerShell, SQL, and more |

## Defining Parameters

### Papermill

In Papermill, parameters are defined by tagging a Jupyter cell with the `parameters` tag. The cell contains Python variable assignments that serve as defaults:

```python
# Parameters
start_date = "2025-01-01"
region = "us-east"
threshold = 0.95
dry_run = True
```

Papermill injects a new cell after the tagged cell at execution time, overriding these values with whatever was passed on the command line.

### Verso

In Verso, parameters are first-class metadata with explicit types, descriptions, defaults, ordering, and a required flag. They are defined in the notebook's `metadata.parameters` section and rendered through a dedicated parameters cell.

In the UI, click **Add Parameter** in the parameters cell (or insert a parameters cell if one does not exist). Each parameter has:

| Field | Description |
|-------|-------------|
| Name | The variable name, used in code cells and `--param` flags |
| Type | One of: `string`, `int`, `float`, `bool`, `date`, `datetime` |
| Description | Optional text shown in the UI and interactive prompts |
| Default | Optional default value (must match the declared type) |
| Required | If checked, execution fails when no value is provided |
| Order | Controls display and prompt ordering |

In the `.verso` file, parameters are stored as structured metadata:

```json
{
  "metadata": {
    "parameters": {
      "startDate": {
        "type": "date",
        "description": "Report start date",
        "default": "2025-01-01",
        "required": true,
        "order": 1
      },
      "region": {
        "type": "string",
        "description": "AWS region",
        "default": "us-east",
        "order": 2
      },
      "threshold": {
        "type": "float",
        "description": "Confidence threshold",
        "default": 0.95,
        "order": 3
      },
      "dryRun": {
        "type": "bool",
        "description": "Skip writes when true",
        "default": true,
        "order": 4
      }
    }
  }
}
```

Parameters are injected into the variable store before any cells execute. Every cell in every language can access them directly by name.

## Supported Parameter Types

| Type | CLR Type | Format | Example |
|------|----------|--------|---------|
| `string` | `string` | Any text | `us-east` |
| `int` | `long` | Integer | `42` |
| `float` | `double` | Decimal number | `0.95` |
| `bool` | `bool` | `true`/`false`/`yes`/`no`/`1`/`0` | `true` |
| `date` | `DateOnly` | `yyyy-MM-dd` (strict) | `2025-01-01` |
| `datetime` | `DateTimeOffset` | ISO 8601 (defaults to UTC if no offset) | `2025-01-01T08:00:00Z` |

Papermill infers types from the Python cell. Verso uses explicit type declarations, which means parameter values are validated and parsed at injection time rather than at runtime.

## Running Parameterized Notebooks

### Papermill

```bash
papermill input.ipynb output.ipynb \
  -p start_date "2025-06-01" \
  -p region "eu-west" \
  -p threshold 0.8 \
  -p dry_run False
```

Papermill reads the input notebook, injects parameters, executes all cells, and writes the output notebook with results.

### Verso

```bash
verso run report.verso \
  --param startDate=2025-06-01 \
  --param region=eu-west \
  --param threshold=0.8 \
  --param dryRun=false
```

Key differences:

- **Syntax:** Verso uses `--param name=value` (equals sign) rather than `-p name value` (space-separated).
- **Type parsing:** Values are parsed according to the parameter's declared type. `0.8` is validated as a `float`, `2025-06-01` as a `date`, and `false` as a `bool`. Type mismatches produce an immediate error before any cells run.
- **Required validation:** If a required parameter has no value and no default, `verso run` exits with a non-zero exit code and lists the missing parameters. This catches configuration errors before execution begins.
- **No output notebook:** By default, `verso run` streams results to the terminal. Use `--output json` to capture structured output, or `--save` to write results back into the notebook file.

### Interactive Mode

Verso supports an interactive mode that Papermill does not have:

```bash
verso run report.verso --interactive
```

When `--interactive` is set and stdin is a terminal, Verso prompts for any parameter that was not provided via `--param` and has no default. This is useful for ad-hoc runs where you want to fill in values at the prompt rather than constructing the full command line.

## CI/CD Migration

### Papermill in GitHub Actions

```yaml
- name: Run report
  run: |
    papermill input.ipynb output.ipynb \
      -p db_host ${{ secrets.DB_HOST }} \
      -p report_date $(date +%Y-%m-%d)
```

### Verso in GitHub Actions

```yaml
- name: Run report
  run: |
    verso run report.verso \
      --param dbHost=${{ secrets.DB_HOST }} \
      --param reportDate=$(date +%Y-%m-%d) \
      --output json > results.json
```

### Papermill in Azure DevOps

```yaml
- script: |
    papermill input.ipynb output.ipynb \
      -p db_host $(DB_HOST) \
      -p report_date $(Build.BuildId)
```

### Verso in Azure DevOps

```yaml
- script: |
    verso run report.verso \
      --param dbHost=$(DB_HOST) \
      --param reportDate=$(Build.BuildId)
```

For sensitive values like database passwords, use environment variables instead of `--param` to keep secrets out of command-line arguments and process listings:

```yaml
- script: |
    verso run report.verso \
      --param dbHost=$(DB_HOST) \
      --param dbName=$(DB_NAME)
  env:
    DB_PASSWORD: $(DB_PASSWORD)
```

Then reference the secret in your notebook's connection string using `$env:DB_PASSWORD`. See the [database connectivity guide](../guides/database-connectivity.md#cicd-pipelines) for full examples.

## Output Handling

### Papermill

Papermill writes an executed copy of the notebook with all cell outputs populated:

```python
import papermill as pm
nb = pm.execute_notebook("input.ipynb", "output.ipynb", parameters={"region": "us-east"})
```

### Verso

Verso offers several output modes:

```bash
# Stream text output to terminal (default)
verso run report.verso --param region=us-east

# Structured JSON output for programmatic consumption
verso run report.verso --param region=us-east --output json > results.json

# Save outputs back into the notebook file
verso run report.verso --param region=us-east --save

# Suppress output entirely (run for side effects only)
verso run report.verso --param region=us-east --output none
```

The `--output json` mode produces structured output suitable for parsing in CI pipelines, with cell IDs, execution status, and output content.

## Additional Verso Capabilities

Features available in Verso that have no direct Papermill equivalent:

### Multi-Language Parameters

Parameters are available to all languages in the notebook, not just Python. A `date` parameter can be used in a SQL query, a C# data pipeline, and a Python visualization, all within the same notebook:

```sql
SELECT * FROM Reports WHERE ReportDate = @startDate
```

```csharp
var data = LoadReport(startDate, region);
```

```python
df = get_dataframe("reports")
filtered = df[df["region"] == region]
```

### Selective Cell Execution

Run specific cells instead of the entire notebook:

```bash
# By cell index (0-based)
verso run report.verso --cell 0 --cell 3 --cell 5

# By cell ID
verso run report.verso --cell "a1b2c3d4-..."
```

### Fail-Fast Mode

Stop execution on the first cell failure instead of continuing:

```bash
verso run report.verso --fail-fast
```

### Timeout Control

Set a maximum execution time (default is 300 seconds):

```bash
verso run report.verso --timeout 600
```

### Database Connectivity

Verso has built-in SQL support with parameterized queries, schema inspection, and EF Core scaffolding. Papermill workflows that use Python database libraries can be migrated to native SQL cells with `@variable` binding for parameters. See the [database connectivity guide](../guides/database-connectivity.md) for details.
