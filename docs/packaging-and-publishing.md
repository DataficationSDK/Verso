# Packaging and Publishing

Verso extensions are distributed as NuGet packages. This guide covers configuring your project for packaging, versioning, publishing to NuGet.org, testing with a local feed, and how the host discovers extensions at runtime.

## Project Configuration

The Verso extension template generates a `.csproj` that is already configured for NuGet packaging. Here are the key properties:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- NuGet package metadata -->
    <PackageId>Verso.Sample.Dice</PackageId>
    <Version>1.0.0</Version>
    <Authors>Verso Contributors</Authors>
    <Description>Sample Verso extension demonstrating dice notation parsing with ILanguageKernel, ICellRenderer, IDataFormatter, and IToolbarAction.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>

    <!-- Build a .nupkg on every build -->
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Verso.Abstractions" Version="0.5.0" />
  </ItemGroup>
</Project>
```

### Required Properties

| Property | Description | Example |
|---|---|---|
| `PackageId` | Unique package identifier on NuGet.org. Use a clear, namespaced name. | `Verso.Sample.Dice` |
| `Version` | Semantic version of the package. | `1.0.0` |
| `Authors` | One or more author names, separated by commas. | `Verso Contributors` |
| `Description` | Short description of what the extension does. Shown on NuGet.org. | `Dice notation parser for Verso notebooks.` |
| `PackageLicenseExpression` | SPDX license identifier. | `MIT` |

### Optional Properties

| Property | Description |
|---|---|
| `PackageProjectUrl` | URL to the project homepage or repository. |
| `PackageIconUrl` / `PackageIcon` | Icon displayed on NuGet.org. |
| `PackageTags` | Space-separated tags for discoverability (e.g., `verso extension notebook dice`). |
| `PackageReleaseNotes` | Release notes for the current version. |
| `RepositoryUrl` | URL to the source repository. |
| `RepositoryType` | Repository type (e.g., `git`). |
| `Copyright` | Copyright statement. |

### GeneratePackageOnBuild

When `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>` is set, every `dotnet build` produces a `.nupkg` in the output directory (e.g., `bin/Debug/`). This is convenient during development. For CI pipelines, you can instead use `dotnet pack` explicitly.

## Dependency Management

Your extension should reference only `Verso.Abstractions`, not the full `Verso` package. This keeps your package lightweight and avoids pulling in host internals:

```xml
<ItemGroup>
  <PackageReference Include="Verso.Abstractions" Version="0.5.0" />
</ItemGroup>
```

Any additional NuGet dependencies your extension needs (e.g., JSON parsing, HTTP clients) are automatically included in the package and resolved at runtime via the isolated `AssemblyLoadContext`.

## Versioning Strategy

Follow [Semantic Versioning 2.0](https://semver.org/):

- **Major** (1.0.0 to 2.0.0): Breaking changes to your extension's public API or behavior.
- **Minor** (1.0.0 to 1.1.0): New features that are backward-compatible.
- **Patch** (1.0.0 to 1.0.1): Bug fixes and minor improvements.

For pre-release versions, use a suffix:

```xml
<Version>1.1.0-beta.1</Version>
```

### Version in Extension Metadata

Keep the `Version` property on your `IExtension` implementation in sync with the NuGet package version:

```csharp
public string Version => "1.1.0-beta.1";
```

This version is displayed in the Verso extension manager UI.

## Building the Package

### During Development

With `GeneratePackageOnBuild` enabled:

```bash
dotnet build
# Output: bin/Debug/Verso.Sample.Dice.1.0.0.nupkg
```

### For Release

Use `dotnet pack` with the Release configuration:

```bash
dotnet pack -c Release
# Output: bin/Release/Verso.Sample.Dice.1.0.0.nupkg
```

To override the version at build time (useful in CI):

```bash
dotnet pack -c Release -p:Version=1.2.0
```

## Local Feed Testing

Before publishing to NuGet.org, test your package locally.

### Step 1: Create a Local Feed Directory

```bash
mkdir -p ~/.nuget/local-feed
```

### Step 2: Add the Local Feed

```bash
dotnet nuget add source ~/.nuget/local-feed --name LocalVersoExtensions
```

### Step 3: Copy Your Package

```bash
cp bin/Release/Verso.Sample.Dice.1.0.0.nupkg ~/.nuget/local-feed/
```

### Step 4: Install and Test

Point Verso at the local feed or install the package into a test project to verify it loads correctly.

### Removing the Local Feed

When done testing:

```bash
dotnet nuget remove source LocalVersoExtensions
```

## Publishing to NuGet.org

### Step 1: Create a NuGet.org Account

Register at [nuget.org](https://www.nuget.org/) and generate an API key with push permissions for your package ID prefix.

### Step 2: Push the Package

```bash
dotnet nuget push bin/Release/Verso.Sample.Dice.1.0.0.nupkg \
    --api-key YOUR_API_KEY \
    --source https://api.nuget.org/v3/index.json
```

### Step 3: Verify

After a few minutes, your package will be indexed and searchable on NuGet.org.

### CI/CD Integration

In a CI pipeline, store the API key as a secret and automate the push:

```yaml
# Example GitHub Actions step
- name: Push to NuGet
  run: |
    dotnet nuget push **/*.nupkg \
      --api-key ${{ secrets.NUGET_API_KEY }} \
      --source https://api.nuget.org/v3/index.json \
      --skip-duplicate
```

## Extension Discovery Mechanism

Understanding how Verso discovers your extension at runtime helps with troubleshooting.

### Assembly Scanning

When Verso loads an extension package, it:

1. Loads the assembly into an isolated `ExtensionLoadContext` (a collectible `AssemblyLoadContext`).
2. Shares `Verso.Abstractions` types from the default context so that interface type identity is preserved.
3. Scans all types in the assembly for the `[VersoExtension]` attribute.
4. Instantiates each attributed class via its parameterless constructor.
5. Calls `OnLoadedAsync(IExtensionHostContext)` on each instance.
6. Categorizes the instance by which capability interfaces it implements (`ILanguageKernel`, `ICellRenderer`, etc.).

### The [VersoExtension] Attribute

Every class that should be discovered must be decorated with `[VersoExtension]`:

```csharp
[VersoExtension]
public sealed class MyKernel : ILanguageKernel
{
    // ...
}
```

Key rules:
- The attribute is required. Without it, the class will not be discovered even if it implements an extension interface.
- Each class must have a public parameterless constructor.
- Multiple `[VersoExtension]` classes can exist in a single assembly (e.g., one for the kernel, one for the formatter, one for the toolbar action).
- The attribute cannot be inherited (`Inherited = false`).

### Assembly Isolation

Each extension assembly loads in its own `AssemblyLoadContext`. This means:
- Your extension's dependencies do not conflict with other extensions or the host.
- `Verso.Abstractions` is shared so that `IExtension`, `ILanguageKernel`, etc. are the same types across all contexts.
- The load context is collectible, so extensions can be unloaded to free memory.

### Troubleshooting

| Symptom | Likely Cause |
|---|---|
| Extension not discovered | Missing `[VersoExtension]` attribute, or missing parameterless constructor. |
| Type cast failures | Extension compiled against a different version of `Verso.Abstractions` than the host. |
| Missing dependencies at runtime | A transitive dependency was not included in the NuGet package. Verify with `dotnet nuget verify`. |
| Extension loads but does nothing | `OnLoadedAsync` is throwing an exception silently. Check host logs. |

---

## See Also

- [Getting Started](getting-started.md) -- scaffolding and initial setup
- [Extension Interfaces](extension-interfaces.md) -- the `[VersoExtension]` attribute and interface reference
- [Testing Extensions](testing-extensions.md) -- testing before publishing
- [Best Practices](best-practices.md) -- naming conventions and ID format
