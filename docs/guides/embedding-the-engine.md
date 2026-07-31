# Embedding the Engine

The Verso engine is a headless .NET library with no UI dependencies. Any .NET application can reference it, create a notebook session, load extensions, and execute cells programmatically. The editor, the CLI, and the VS Code host are all just consumers of this same API, so anything they do is available to your own code.

This is an advanced scenario. For the full picture of the engine's internals, see the [Engine](../architecture/engine.md) and [Execution Pipeline](../architecture/execution-pipeline.md) architecture pages. This guide is a task-focused starting point.

## Creating a session

`Scaffold` is the central object for a notebook session. It owns the in-memory notebook model, the kernel registry, execution dispatch, and the theme, layout, and settings subsystems.

```csharp
using Verso;
using Verso.Extensions;

// Load the built-in extensions (kernels, themes, layouts, formatters)
var extensionHost = new ExtensionHost();
await extensionHost.LoadBuiltInExtensionsAsync();

// Create a session over a blank notebook and initialize subsystems
var scaffold = new Scaffold(new NotebookModel(), extensionHost, filePath: null);
scaffold.InitializeSubsystems();
```

`InitializeSubsystems()` must be called after extensions are loaded. It queries the host for themes, layouts, and settable extensions and wires up the subsystems, refreshing automatically if extensions are added or toggled later.

## Executing code

You can execute a specific cell, the whole notebook, or arbitrary code that is never added to the notebook:

```csharp
// Execute arbitrary code in a kernel and capture its outputs
var outputs = await scaffold.ExecuteCodeCaptureOutputsAsync(
    "1 + 41", language: "csharp");

foreach (var output in outputs)
    Console.WriteLine(output.Content);
```

To work with a real notebook, add cells and run them:

```csharp
scaffold.AddCell("code", language: "csharp", source: "var greeting = \"hello\";");
scaffold.AddCell("code", language: "csharp", source: "greeting.ToUpper()");

await scaffold.ExecuteAllAsync();
```

`ExecuteAllAsync` resets the kernels first so a full run behaves as if the notebook were executed from scratch. `ExecuteCellAsync(cellId)` runs a single cell without resetting.

## Sharing variables

All kernels in a session share one variable store, so a value set by one language is visible to every other. Your host code can read and write it directly:

```csharp
scaffold.Variables.Set("threshold", 0.95);
var threshold = scaffold.Variables.Get<double>("threshold");
```

## Observing execution

Subscribe to Scaffold events to react to execution and kernel state, for example to drive a progress display. The cell events carry the cell's id, and by the time `OnCellExecuted` fires the cell's `ExecutionCount`, `LastElapsed`, and `LastStatus` have been stamped, so look the cell up to read them:

```csharp
scaffold.OnCellExecuted += cellId =>
{
    var cell = scaffold.GetCell(cellId);
    Console.WriteLine($"Cell {cellId} finished: {cell?.LastStatus} in {cell?.LastElapsed}");
};
```

`OnCellExecuting` and `OnCellOutputUpdated` have the same `Action<Guid>` shape. The kernel-restart events (`OnKernelRestarting`, `OnKernelRestarted`, `OnKernelRestartFailed`) carry the kernel's language name instead.

`ExecuteCellAsync` and `ExecuteAllAsync` also return `ExecutionResult` values directly, which is the simpler route when you only need the outcome of a call you made yourself rather than notification of any execution.

## Cleaning up

`Scaffold` implements `IAsyncDisposable`. Disposing it disposes every registered kernel, clears internal registries, and disposes the extension host, which unloads all extensions:

```csharp
await scaffold.DisposeAsync();
```

## Loading more extensions

Beyond the built-ins, you can load a third-party extension assembly into an isolated context:

```csharp
await extensionHost.LoadFromAssemblyAsync("./MyExtension.dll");
```

Third-party assemblies load into a collectible `AssemblyLoadContext` that isolates their dependencies while sharing `Verso.Abstractions` types with the host. See [Managing Extensions](managing-extensions.md) for the packaged-extension workflow and [Extension Host](../architecture/extension-host.md) for how discovery and isolation work.
