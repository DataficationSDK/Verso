# Extension Host

The `ExtensionHost` (namespace `Verso.Extensions`) is responsible for discovering, loading, validating, and managing all extensions in a Verso session. It implements `IExtensionHostContext` (the read-only query surface exposed to extensions) and `IAsyncDisposable`.

## Discovery

Extension discovery happens at startup via `LoadBuiltInExtensionsAsync()`. The process has two phases:

### Phase 1: Scan the Engine Assembly

The host scans `typeof(ExtensionHost).Assembly` (the Verso engine DLL) for classes decorated with `[VersoExtension]` that implement `IExtension`. This picks up all built-in extensions: the C# kernel, markdown renderer, themes, layouts, formatters, and magic commands.

### Phase 2: Scan Co-Deployed Assemblies

The host iterates all `*.dll` files in `AppContext.BaseDirectory`, skipping:
- Test assemblies (names ending in `.Tests`)
- Resource satellite assemblies (names ending in `.resources`)
- `Verso.Abstractions.dll` itself

For each candidate, the host uses `PEReader` and `MetadataReader` to check whether the DLL references `Verso.Abstractions` **without loading the assembly**. This PE metadata check is fast and avoids loading hundreds of irrelevant System/Microsoft DLLs into memory.

Only assemblies that reference `Verso.Abstractions` are loaded via `Assembly.LoadFrom()` into the **default** `AssemblyLoadContext`. This means built-in and co-deployed extensions share the host's type identities with no isolation boundary.

Each loaded assembly is scanned for `[VersoExtension]` classes, which are instantiated with `Activator.CreateInstance()` and passed through validation.

## Loading Third-Party Extensions

Third-party extensions are loaded through `LoadFromAssemblyAsync(path)` or `LoadFromDirectoryAsync(directoryPath)`. These use a different loading strategy than built-in extensions.

### Isolated Assembly Load Context

Each third-party assembly is loaded into its own `ExtensionLoadContext`, a collectible `AssemblyLoadContext` subclass. This provides:

- **Type identity**: The load context overrides `Load(AssemblyName)` to return the host's `Verso.Abstractions` assembly directly. This ensures that interface types (like `ILanguageKernel`) are shared between the extension and the host, regardless of which version of Abstractions the extension was compiled against.
- **Dependency isolation**: For all other assemblies, the load context uses `AssemblyDependencyResolver` (seeded from the extension DLL's `.deps.json`) to resolve extension-local dependencies. This prevents dependency conflicts between extensions or with the host.
- **Unloadability**: The context is collectible, meaning it can be unloaded when the extension is removed, freeing the memory used by the extension's assemblies.

### Version Compatibility

Before loading, the host checks version compatibility by inspecting the extension assembly's reference to `Verso.Abstractions`:

- The **major version** must match exactly
- The extension's **minor version** must be less than or equal to the host's minor version

This ensures extensions can use any API available at their compile-time version, but cannot depend on APIs introduced in a newer minor version.

## Validation

Every extension passes through `ValidateExtension()` before loading. The checks are:

| Rule | Error |
|------|-------|
| `ExtensionId` must be non-empty | `MissingId` |
| `ExtensionId` must be unique | `DuplicateId` |
| `Name` must be non-empty | `MissingName` |
| `Version` must be present and valid semver | `InvalidVersion` |
| Must implement at least one capability interface | `NoCapability` |

During auto-discovery (built-in scanning), validation errors are silently skipped. When loading explicitly (via magic command or API), validation failures throw `ExtensionLoadException`.

## Registration

After validation, `LoadExtensionAsync(extension)` runs:

1. Calls `extension.OnLoadedAsync(this)` to give the extension a chance to initialize
2. Adds the extension to the master `_extensions` list
3. Calls `AutoRegister(extension)` to add the extension to every capability-specific list it qualifies for

`AutoRegister` checks each capability interface with `is` pattern matching:

```csharp
if (extension is ILanguageKernel kernel) _kernels.Add(kernel);
if (extension is ICellRenderer renderer) _renderers.Add(renderer);
if (extension is IDataFormatter formatter) _formatters.Add(formatter);
if (extension is ICellPropertyProvider provider) _propertyProviders.Add(provider);
// ... and so on for all capability interfaces
```

A single extension can implement multiple interfaces and will be registered in all matching lists. For example, `ParametersCellRenderer` implements both `ICellRenderer` and `ICellInteractionHandler`.

After registration, the `OnExtensionLoaded` event fires. `Scaffold.InitializeSubsystems()` subscribes to this event to refresh themes, layouts, and settings when new extensions appear.

## Enable and Disable

Extensions can be enabled and disabled at runtime without unloading them:

```csharp
await extensionHost.DisableExtensionAsync("verso.theme.dark");
await extensionHost.EnableExtensionAsync("verso.theme.dark");
```

Disabled extensions remain in the `_extensions` list (visible in `GetLoadedExtensions()`) but are filtered out of all capability queries (`GetKernels()`, `GetRenderers()`, etc.). The `OnExtensionStatusChanged` event fires on state changes, triggering subsystem refresh.

## Query API

`IExtensionHostContext` provides typed query methods that return only enabled extensions:

| Method | Returns |
|--------|---------|
| `GetLoadedExtensions()` | All extensions (enabled and disabled) |
| `GetKernels()` | `IReadOnlyList<ILanguageKernel>` |
| `GetRenderers()` | `IReadOnlyList<ICellRenderer>` |
| `GetFormatters()` | `IReadOnlyList<IDataFormatter>` |
| `GetCellTypes()` | `IReadOnlyList<ICellType>` |
| `GetSerializers()` | `IReadOnlyList<INotebookSerializer>` |
| `GetLayouts()` | `IReadOnlyList<ILayoutEngine>` |
| `GetThemes()` | `IReadOnlyList<ITheme>` |
| `GetPostProcessors()` | `IReadOnlyList<INotebookPostProcessor>` |
| `GetSettableExtensions()` | `IReadOnlyList<IExtensionSettings>` |
| `GetPropertyProviders()` | `IReadOnlyList<ICellPropertyProvider>` |
| `GetExtensionInfos()` | Metadata for all extensions with status |

Additional methods not on `IExtensionHostContext` (used internally by the engine):

| Method | Returns |
|--------|---------|
| `GetToolbarActions()` | `IReadOnlyList<IToolbarAction>` |
| `GetMagicCommands()` | `IReadOnlyList<IMagicCommand>` |
| `GetInteractionHandler(extensionId)` | `ICellInteractionHandler?` matched by `ExtensionId` |

## Consent

When a third-party extension package is loaded from NuGet (via the `#!extension` magic command), the host can request user consent before proceeding:

```csharp
extensionHost.ConsentHandler = async (packages, ct) => {
    // Show approval dialog, return true/false
};
```

The hosting layer sets this delegate. In Blazor Server, it triggers the `ExtensionConsentDialog` component. In VS Code, it sends a consent request notification to the webview. Local DLL paths skip consent.

Package approval is tracked per session via `IsPackageApproved(packageId)` and `ApprovePackage(packageId)`. Package loading is idempotent via `IsExtensionPackageLoaded(packageId)`.

## Lifecycle

### Extension Lifecycle

Each extension goes through:

1. **Construction** -- `Activator.CreateInstance()` during discovery
2. **OnLoadedAsync** -- called with the `IExtensionHostContext`, giving the extension access to query other extensions
3. **Active use** -- capability methods called by the engine and front-ends
4. **OnUnloadedAsync** -- called during teardown
5. **Dispose** -- `IAsyncDisposable.DisposeAsync()` or `IDisposable.Dispose()` if implemented

### Host Teardown

`ExtensionHost.DisposeAsync()` delegates to `UnloadAllAsync()`, which:

1. Iterates extensions in reverse load order
2. Calls `OnUnloadedAsync()` on each
3. Calls `DisposeAsync()` or `Dispose()` if implemented
4. Calls `Unload()` on all `ExtensionLoadContext` instances

The reverse-order teardown ensures extensions loaded later (which may depend on earlier ones) are cleaned up first. Unloading the collectible `AssemblyLoadContext` instances allows the GC to reclaim the memory used by third-party extension assemblies.

## VersoExtension Attribute

`[VersoExtension]` (namespace `Verso.Abstractions.Attributes`) is a simple marker attribute:

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class VersoExtensionAttribute : Attribute { }
```

It has no properties. Its presence on a class, combined with that class implementing `IExtension`, is the sole discovery criterion. The `Inherited = false` setting means subclasses of an extension class are not automatically discovered.
