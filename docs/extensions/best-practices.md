# Best Practices

This guide covers conventions, patterns, and pitfalls for Verso extension development. Following these practices will help you write extensions that are reliable, performant, and consistent with the platform.

## ID Naming Conventions

### Extension IDs

Use reverse-domain notation for all identifiers. This prevents collisions across independent extension authors.

```csharp
// Good
public string ExtensionId => "com.mycompany.dice";
public string ExtensionId => "com.mycompany.dice.formatter";
public string ExtensionId => "org.community.sqlkernel";

// Bad
public string ExtensionId => "dice";
public string ExtensionId => "my-extension";
```

If your extension provides multiple capabilities (kernel, renderer, formatter, toolbar action), give each `[VersoExtension]` class a distinct `ExtensionId` under a shared prefix:

```csharp
// Kernel
public string ExtensionId => "com.mycompany.dice";

// Formatter
public string ExtensionId => "com.mycompany.dice.formatter";

// Renderer
public string ExtensionId => "com.mycompany.dice.renderer";

// Toolbar action
public string ExtensionId => "com.mycompany.dice.rollall";
```

### Language IDs

Use short, lowercase identifiers for `LanguageId`:

```csharp
public string LanguageId => "dice";      // Good
public string LanguageId => "Dice Lang"; // Bad - spaces and mixed case
```

### Action IDs

Use a dot-separated namespace for `ActionId`:

```csharp
public string ActionId => "dice.action.roll-all";   // Good
public string ActionId => "RollAll";                  // Bad - not namespaced
```

## Thread Safety

Verso may call your extension methods concurrently. This is especially relevant for kernels (multiple cells may be queued for execution) and formatters (multiple outputs may be formatted simultaneously).

### Guarding Shared State

If your extension maintains mutable state, protect it with locks or use concurrent collections:

```csharp
[VersoExtension]
public sealed class MyKernel : ILanguageKernel
{
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private int _executionCount;

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        await _executionLock.WaitAsync(context.CancellationToken);
        try
        {
            _executionCount++;
            // ... execute code ...
        }
        finally
        {
            _executionLock.Release();
        }
    }
}
```

### Stateless Implementations

Where possible, keep your extensions stateless. The Dice sample demonstrates this pattern -- `DiceRenderer` and `DiceFormatter` hold no mutable state and are inherently thread-safe.

### Random Number Generators

If using `Random`, be aware that `System.Random` is not thread-safe prior to .NET 6. On .NET 8, `Random.Shared` is the preferred thread-safe option:

```csharp
// Good - thread-safe on .NET 8
var roll = Random.Shared.Next(1, sides + 1);

// Risky - private instance may be called from multiple threads
private readonly Random _rng = new();
```

## Theme-Aware Rendering

Extensions that produce HTML output should adapt to the active theme. Use `IThemeContext` (available via `context.Theme`) rather than hardcoding colors.

### Reading Theme Colors

```csharp
public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
{
    var bg = context.Theme.GetColor("editor.background");
    var fg = context.Theme.GetColor("editor.foreground");
    var border = context.Theme.GetColor("border");
    var font = context.Theme.GetFont("code");

    var html = $"""
        <pre style="background:{bg};color:{fg};border:1px solid {border};
                    font-family:{font.Family};font-size:{font.Size}px;
                    padding:8px;border-radius:4px;">
            <code>{HttpUtility.HtmlEncode(source)}</code>
        </pre>
        """;
    return Task.FromResult(new RenderResult("text/html", html));
}
```

### Branching on ThemeKind

For cases where you need fundamentally different markup:

```csharp
var isDark = context.Theme.ThemeKind == ThemeKind.Dark;
var highlightColor = isDark ? "#2e7d32" : "#1b5e20";
```

### Custom Theme Tokens

If your extension defines its own color scheme, register custom tokens via `ITheme.GetCustomToken` and consume them through `IThemeContext.GetCustomToken`:

```csharp
// In your renderer
var diceMaxColor = context.Theme.GetCustomToken("dice.maxRoll.color") ?? "#2e7d32";
```

This allows theme authors to override your extension's colors.

## Error Handling

### Returning Errors from Kernels

Use `CellOutput` with `IsError = true` rather than throwing exceptions from `ExecuteAsync`. Thrown exceptions propagate to the host and may result in a generic error message:

```csharp
// Good - structured error output
public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
{
    try
    {
        // ... execute ...
    }
    catch (ParseException ex)
    {
        return Task.FromResult<IReadOnlyList<CellOutput>>(new[]
        {
            new CellOutput("text/plain", ex.Message,
                IsError: true,
                ErrorName: ex.GetType().Name,
                ErrorStackTrace: ex.StackTrace)
        });
    }
}

// Bad - unhandled exception
public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
{
    var result = Parse(code); // May throw
    return Task.FromResult<IReadOnlyList<CellOutput>>(new[] { ... });
}
```

### Handling Errors in Lifecycle Methods

`OnLoadedAsync` and `OnUnloadedAsync` should not throw. If initialization can fail, catch the exception and degrade gracefully:

```csharp
public async Task OnLoadedAsync(IExtensionHostContext context)
{
    try
    {
        await InitializeExternalService();
    }
    catch (Exception ex)
    {
        // Log the failure; the extension will load but some features may be unavailable
        _initializationError = ex;
    }
}
```

### Respecting CancellationToken

Always honor the `CancellationToken` from the context in long-running operations:

```csharp
public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
{
    foreach (var line in code.Split('\n'))
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        await ProcessLineAsync(line);
    }
    // ...
}
```

Pass the token to any downstream async operations:

```csharp
var response = await httpClient.GetAsync(url, context.CancellationToken);
```

## Performance

### Avoid Blocking Calls

Never use `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` inside async extension methods. These can deadlock the host:

```csharp
// Bad - synchronous blocking in an async method
public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
{
    var data = httpClient.GetStringAsync(url).Result; // DEADLOCK RISK
    // ...
}

// Good - proper await
public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
{
    var data = await httpClient.GetStringAsync(url, context.CancellationToken);
    // ...
}
```

### Minimize Allocations in Formatters

Formatters may be called frequently (once per displayed object). Avoid allocating large strings or collections unnecessarily:

```csharp
// Good - use StringBuilder for HTML construction
public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
{
    var sb = new StringBuilder(256); // Pre-size when you can estimate
    sb.Append("<table>");
    // ... build HTML ...
    sb.Append("</table>");
    return Task.FromResult(new CellOutput("text/html", sb.ToString()));
}

// Bad - repeated string concatenation
public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
{
    var html = "<table>";
    foreach (var item in collection)
        html += $"<tr><td>{item}</td></tr>"; // Allocates on every iteration
    html += "</table>";
    return Task.FromResult(new CellOutput("text/html", html));
}
```

### Respect Size Constraints

Check `IFormatterContext.MaxWidth` and `IFormatterContext.MaxHeight` to avoid producing oversized output. For large collections, truncate and provide a summary:

```csharp
public Task<CellOutput> FormatAsync(object value, IFormatterContext context)
{
    var list = (IList)value;
    var displayCount = Math.Min(list.Count, 100);

    var sb = new StringBuilder();
    for (var i = 0; i < displayCount; i++)
        sb.AppendLine(list[i]?.ToString());

    if (list.Count > displayCount)
        sb.AppendLine($"... and {list.Count - displayCount} more items");

    return Task.FromResult(new CellOutput("text/plain", sb.ToString()));
}
```

### Lazy Initialization

Defer expensive setup to `InitializeAsync` (for kernels) or the first call, rather than doing it in the constructor:

```csharp
[VersoExtension]
public sealed class HeavyKernel : ILanguageKernel
{
    private ScriptEngine? _engine;

    // Constructor is lightweight - called during assembly scanning
    public HeavyKernel() { }

    // Heavy setup happens here, called once before first execution
    public async Task InitializeAsync()
    {
        _engine = await ScriptEngine.CreateAsync();
    }
}
```

### ConfigureAwait(false)

If your extension code does not need to return to a synchronization context, use `ConfigureAwait(false)` on awaited tasks to avoid unnecessary context switching:

```csharp
var data = await httpClient.GetStringAsync(url, context.CancellationToken)
    .ConfigureAwait(false);
```

## What NOT to Do

### Do Not Hold References to Context Objects

Context objects are scoped to a single operation. Do not store them in fields:

```csharp
// Bad - context is only valid during the method call
private IExecutionContext? _lastContext;

public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
{
    _lastContext = context; // Do not do this
    // ...
}
```

### Do Not Modify NotebookCells Directly

The `NotebookCells` list from `IToolbarActionContext` is read-only. Use `context.Notebook.InsertCellAsync`, `RemoveCellAsync`, and `MoveCellAsync` to mutate the notebook:

```csharp
// Bad
context.NotebookCells.Add(new CellModel()); // Will not compile - IReadOnlyList

// Good
await context.Notebook.InsertCellAsync(0, "code", "csharp");
```

### Do Not Perform File I/O Without Cancellation

If your extension reads or writes files, always pass the cancellation token:

```csharp
// Bad
var content = await File.ReadAllTextAsync(path);

// Good
var content = await File.ReadAllTextAsync(path, context.CancellationToken);
```

### Do Not Throw from CanFormat

`IDataFormatter.CanFormat` should never throw. Return `false` for values you cannot handle:

```csharp
// Good
public bool CanFormat(object value, IFormatterContext context)
{
    return value is DiceResult;
}

// Bad - may throw on unexpected types
public bool CanFormat(object value, IFormatterContext context)
{
    return ((DiceResult)value).Rolls.Count > 0; // InvalidCastException risk
}
```

### Do Not Assume a Specific Host

Extensions may run in different hosts (VS Code, Blazor WebAssembly, standalone). Do not assume:
- File system access is available (Blazor Wasm has no local file system).
- `RequestFileDownloadAsync` is supported (check for `NotSupportedException`).
- Console output is visible (use `WriteOutputAsync` or `DisplayAsync` instead of `Console.WriteLine`).

## Extension Lifecycle Summary

| Phase | What Happens | Your Responsibility |
|---|---|---|
| Discovery | Host scans for `[VersoExtension]` classes | Ensure attribute is present, constructor is parameterless |
| Loading | `OnLoadedAsync` is called | Register services, subscribe to events |
| Initialization | `InitializeAsync` (kernels only) | Set up runtime, load resources |
| Active Use | Methods called as needed | Handle errors, respect cancellation |
| Unloading | `OnUnloadedAsync` is called | Release resources, unsubscribe from events |
| Disposal | `DisposeAsync` (kernels only) | Dispose runtime, clear caches |

---

## See Also

- [Getting Started](getting-started.md) -- project setup and scaffolding
- [Extension Interfaces](extension-interfaces.md) -- interface member reference
- [Context Reference](context-reference.md) -- what each context provides
- [Testing Extensions](testing-extensions.md) -- testing patterns for each interface
- [Packaging and Publishing](packaging-and-publishing.md) -- versioning and NuGet workflow
