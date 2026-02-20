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

The template creates a solution with an extension project referencing `Verso.Abstractions` and a companion test project referencing `Verso.Testing`.

See the [getting started guide](https://github.com/DataficationSDK/Verso/blob/main/docs/getting-started.md) for full documentation.
