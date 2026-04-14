# Verso.Templates

Project templates for building [Verso](https://github.com/DataficationSDK/Verso) extensions.

## Installation

```shell
dotnet new install Verso.Templates
```

## Usage

```shell
# Create a new extension project
dotnet new verso-extension -n MyExtension

# Build and test
cd MyExtension
dotnet build
dotnet test
```

The template creates an extension project referencing `Verso.Abstractions` and a companion test project referencing `Verso.Testing`.

### Template Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `--extensionId` | string | The unique extension identifier (reverse-domain format) |
| `--author` | string | The extension author name |
| `--includeKernel` | bool | Include an `ILanguageKernel` scaffold |
| `--includeRenderer` | bool | Include an `ICellRenderer` scaffold |
| `--includeMagicCommand` | bool | Include an `IMagicCommand` scaffold |
| `--includeCellType` | bool | Include an `ICellType` scaffold |
| `--includeTheme` | bool | Include an `ITheme` scaffold |

See the [getting started guide](https://github.com/DataficationSDK/Verso/blob/main/docs/getting-started.md) for full documentation.
