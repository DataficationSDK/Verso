# Getting Started with Verso Extensions

This guide walks you through creating your first Verso extension, from setting up your development environment to loading the extension in dev mode.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- A code editor (Visual Studio, VS Code, or Rider)
- Familiarity with C# and async/await

Verify your SDK installation:

```bash
dotnet --version
# Should output 8.0.x or higher
```

## Installing the Verso Extension Template

Install the Verso project template from NuGet:

```bash
dotnet new install Verso.Templates
```

This registers the `verso-extension` template. Confirm it is available:

```bash
dotnet new list verso
```

You should see a row for "Verso Extension" with short name `verso-extension`.

## Scaffolding a New Extension

Create a new extension project:

```bash
dotnet new verso-extension -n MyDashboard \
    --extensionId com.mycompany.dashboard \
    --author "Your Name"
```

### Template Options

| Option | Description | Default |
|---|---|---|
| `-n` / `--name` | Project and namespace name | `MyExtension` |
| `--extensionId` | Unique extension ID in reverse-domain format | `com.example.myextension` |
| `--author` | Author name embedded in the extension metadata | `Extension Author` |
| `--include-kernel` | Scaffold an `ILanguageKernel` implementation | `false` |
| `--include-renderer` | Scaffold an `ICellRenderer` implementation | `false` |

To include a language kernel and cell renderer:

```bash
dotnet new verso-extension -n DiceRoller \
    --extensionId com.mycompany.diceroller \
    --author "Your Name" \
    --include-kernel \
    --include-renderer
```

## Project Structure

After scaffolding, you will have this layout:

```
MyDashboard/
  MyDashboard.cs            # IExtension entry point marked with [VersoExtension]
  SampleFormatter.cs        # IDataFormatter scaffold
  SampleToolbarAction.cs    # IToolbarAction scaffold
  SampleKernel.cs           # (if --include-kernel) ILanguageKernel scaffold
  SampleRenderer.cs         # (if --include-renderer) ICellRenderer scaffold
  GlobalUsings.cs           # using Verso.Abstractions;
  MyDashboard.csproj        # Project file referencing Verso.Abstractions
  MyDashboard.Tests/
    SampleFormatterTests.cs
    SampleToolbarActionTests.cs
    GlobalUsings.cs
    MyDashboard.Tests.csproj
```

The generated `.csproj` references the `Verso.Abstractions` NuGet package and enables `GeneratePackageOnBuild`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>MyDashboard</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
    <Description>A Verso extension.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Verso.Abstractions" Version="0.5.0" />
  </ItemGroup>
</Project>
```

## The Extension Entry Point

Every extension needs at least one class implementing `IExtension` and decorated with `[VersoExtension]`. The template generates this for you:

```csharp
[VersoExtension]
public sealed class MyDashboardEntry : IExtension
{
    public string ExtensionId => "com.mycompany.dashboard";
    public string Name => "MyDashboard";
    public string Version => "1.0.0";
    public string? Author => "Your Name";
    public string? Description => "A Verso extension.";

    public Task OnLoadedAsync(IExtensionHostContext context)
    {
        // Called when the extension is loaded by the host.
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync()
    {
        // Called when the extension is unloaded.
        return Task.CompletedTask;
    }
}
```

Classes that implement capability interfaces (`ILanguageKernel`, `ICellRenderer`, `IDataFormatter`, `IToolbarAction`, etc.) are each independently marked with `[VersoExtension]` and discovered separately. See [Extension Interfaces](extension-interfaces.md) for the full list.

## Building

Build both the extension and its test project:

```bash
dotnet build MyDashboard/
dotnet build MyDashboard/MyDashboard.Tests/
```

The build output (DLL) will be in `bin/Debug/net8.0/`. Because `GeneratePackageOnBuild` is set, a `.nupkg` is also produced on each build.

## Running Tests

The test project uses MSTest and references `Verso.Testing` for stub contexts:

```bash
dotnet test MyDashboard/MyDashboard.Tests/
```

See [Testing Extensions](testing-extensions.md) for details on writing tests with the stub and fake helpers.

## Loading in Dev Mode

To load your extension into a running Verso instance during development, point Verso at your build output directory. There are two approaches:

### Option 1: Extension directory path

Configure Verso to scan a directory containing your compiled DLL:

```bash
verso --extension-path ./MyDashboard/bin/Debug/net8.0/
```

### Option 2: Local NuGet feed

Add your build output as a local NuGet feed and install from it:

```bash
dotnet nuget add source ./MyDashboard/bin/Debug/ --name LocalExtensions
```

Verso will pick up the package when it resolves extensions. See [Packaging and Publishing](packaging-and-publishing.md) for the full NuGet workflow.

## Extension Discovery

Verso discovers extensions by scanning assemblies for classes decorated with `[VersoExtension]`. Each attributed class is loaded into an isolated `AssemblyLoadContext` that shares only `Verso.Abstractions` types with the host. This ensures your extension's dependencies do not conflict with other extensions or with Verso itself.

The host calls `OnLoadedAsync` on each discovered `IExtension` instance, then categorizes it by the capability interfaces it implements (e.g., `ILanguageKernel`, `ICellRenderer`).

## Next Steps

- [Extension Interfaces](extension-interfaces.md) -- reference for all extension interfaces
- [Context Reference](context-reference.md) -- what each context object provides
- [Testing Extensions](testing-extensions.md) -- writing unit tests with Verso.Testing
- [Packaging and Publishing](packaging-and-publishing.md) -- NuGet packaging workflow
- [Best Practices](best-practices.md) -- naming, thread safety, and performance guidance

For a complete working example, see the Dice sample extension at `samples/SampleExtension/Verso.Sample.Dice/`.
