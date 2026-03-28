# Verso.Cli

Command-line tool for the [Verso](https://github.com/datafication-dev/verso) interactive notebook platform. Launch the Verso editor from your terminal, execute notebooks headlessly in CI pipelines, and convert between notebook formats.

## Installation

```bash
# Global install
dotnet tool install -g Verso.Cli

# Local install (per-project)
dotnet tool install Verso.Cli

# Update
dotnet tool update -g Verso.Cli
```

Requires .NET 8.0 SDK or later.

## Commands

### `verso serve`

Launches the Verso editor as a local web server.

```bash
# Start the editor
verso serve

# Open a specific notebook
verso serve my-notebook.verso

# Use a custom port
verso serve --port 8080

# Skip auto-opening the browser
verso serve --no-browser
```

| Option | Default | Description |
|--------|---------|-------------|
| `<notebook>` | none | Path to a `.verso`, `.ipynb`, or `.dib` file to open on launch |
| `--port` | 5050 | TCP port for the local server |
| `--no-browser` | false | Suppress automatic browser launch |
| `--no-https` | false | Serve over HTTP only |
| `--extensions <dir>` | none | Additional directory to scan for extension assemblies |
| `--verbose` | false | Enable detailed startup logging |

### `verso run`

Executes a notebook headlessly and streams cell outputs to the terminal.

```bash
# Run a notebook
verso run pipeline.verso

# Run with parameters
verso run pipeline.verso --param region=us-east --param date=2024-01-01

# JSON output for CI integration
verso run pipeline.verso --output json --output-file results.json

# Stop on first failure
verso run pipeline.verso --fail-fast

# Run a single cell
verso run notebook.verso --cell 0
```

| Option | Default | Description |
|--------|---------|-------------|
| `<notebook>` | required | Path to a `.verso`, `.ipynb`, or `.dib` file |
| `--param <name=value>` | none | Set a notebook parameter (repeatable) |
| `--output <format>` | `text` | Output format: `text`, `json`, or `none` |
| `--output-file <path>` | none | Write output to a file instead of stdout |
| `--cell <id-or-index>` | none | Execute only the specified cell (repeatable) |
| `--fail-fast` | false | Stop on the first cell failure |
| `--timeout <seconds>` | 300 | Maximum total execution time |
| `--save` | false | Save updated outputs back to the notebook file |
| `--interactive` | false | Prompt for missing required parameters on stdin |
| `--verbose` | false | Print cell execution progress to stderr |
| `--include-markdown` | false | Include rendered markdown content in output |
| `--show-parameters` | false | Show resolved parameter values in output |
| `--extensions <dir>` | none | Additional directory to scan for extension assemblies |

### `verso convert`

Converts a notebook between supported formats.

```bash
# Convert Jupyter to Verso
verso convert notebook.ipynb --to verso

# Convert with a specific output path
verso convert notebook.verso --to ipynb --output exported.ipynb

# Strip outputs for clean version control
verso convert notebook.verso --to verso --strip-outputs
```

### `verso info`

Displays version, runtime, and extension information.

```
$ verso info
Verso CLI 1.0.0
Runtime:    .NET 8.0.11
Engine:     Verso 1.0.0
Extensions:
  verso.kernel.csharp          C# Kernel              1.0.0
  verso.fsharp.kernel          F# Kernel              1.0.0
  verso.python.kernel          Python Kernel           1.0.0
  verso.kernel.javascript      JavaScript Kernel       1.0.0
  verso.powershell.kernel      PowerShell Kernel       1.0.0
  verso.ado.kernel.sql         SQL Kernel              1.0.0
```

## Notebook Parameters

Notebooks can declare typed parameters that are injected before execution. This turns notebooks into reusable, parameterized templates for CI pipelines, scheduled jobs, and automated workflows.

### Defining Parameters

Parameters are declared in the notebook's `metadata.parameters` section:

```json
{
  "verso": "1.0",
  "metadata": {
    "title": "Regional Pipeline",
    "defaultKernel": "csharp",
    "parameters": {
      "region": {
        "type": "string",
        "description": "AWS region to process",
        "default": "us-west-2",
        "required": true
      },
      "batchSize": {
        "type": "int",
        "default": 1000
      },
      "dryRun": {
        "type": "bool",
        "default": false
      }
    }
  }
}
```

Parameters can also be managed visually through the built-in **parameters cell** in the Verso editor.

### Supported Types

| Type | CLR Type | Example |
|------|----------|---------|
| `string` | `string` | `--param region=us-east` |
| `int` | `long` | `--param batchSize=1000` |
| `float` | `double` | `--param threshold=0.95` |
| `bool` | `bool` | `--param dryRun=true` |
| `date` | `DateOnly` | `--param date=2024-01-01` |
| `datetime` | `DateTimeOffset` | `--param ts=2024-01-01T08:00:00Z` |

### Using Parameters in Code

Parameters are injected into the variable store as typed CLR objects and are available as top-level variables in C# cells:

```csharp
// Access directly as a variable
Console.WriteLine($"Processing {region} with batch size {batchSize}");

// Or via the variable store API
var region = Variables.Get<string>("region");
```

```sql
-- SQL cells resolve @paramName from the variable store
SELECT * FROM events
WHERE region = @region AND event_date = @date
```

### Required Parameter Validation

When a parameter is marked `required: true` and has no default value, execution is blocked until a value is provided. From the CLI, missing required parameters produce exit code 5 with a descriptive message:

```
Error: Missing required notebook parameters:

  date (date)     Processing date
  region (string) AWS region to process

Supply values with --param or use --interactive to be prompted.
```

In the Verso editor, required parameters without values display a validation error on the parameters cell when "Run All" is clicked.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All executed cells succeeded |
| 1 | One or more cells failed |
| 2 | Execution timed out |
| 3 | Notebook file not found or unreadable |
| 4 | Serialization error (invalid notebook format) |
| 5 | Missing required notebook parameters |

## CI/CD Integration

### GitHub Actions

```yaml
- name: Install Verso CLI
  run: dotnet tool install -g Verso.Cli

- name: Run notebook
  run: verso run tests/integration.verso --output json --output-file results.json --fail-fast

- name: Upload results
  uses: actions/upload-artifact@v4
  with:
    name: notebook-results
    path: results.json
```

### Parameterized Matrix Build

```yaml
jobs:
  run-pipeline:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        region: [us-east, us-west, eu-west]
    steps:
      - uses: actions/checkout@v4

      - name: Install Verso CLI
        run: dotnet tool install -g Verso.Cli

      - name: Run regional pipeline
        run: >-
          verso run pipelines/regional-etl.verso
          --param region=${{ matrix.region }}
          --param date=${{ github.event.inputs.date }}
          --param batchSize=5000
          --output json
          --output-file results-${{ matrix.region }}.json
          --fail-fast
```

## Built-in Extensions

The CLI ships with all built-in Verso extensions, discovered automatically at runtime:

- **C#** (Roslyn scripting)
- **F#** (F# Interactive)
- **Python** (pythonnet)
- **JavaScript/TypeScript** (Jint)
- **PowerShell**
- **SQL** (ADO.NET, multi-provider)
- **HTTP** (REST client cells)

Additional extensions can be loaded from a directory with `--extensions <dir>`.

## License

MIT
