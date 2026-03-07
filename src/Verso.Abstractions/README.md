# Verso.Abstractions

Pure interfaces and types for the [Verso](https://github.com/DataficationSDK/Verso) extensible notebook platform.

## Overview

This package contains the extension interfaces that define every point of extensibility in Verso. Extension authors reference **only this package**, with no dependency on the engine or any front-end.

### Extension Interfaces

| Interface | Purpose |
|-----------|---------|
| `ICellType` | Pair a renderer with an optional kernel for a new cell type |
| `ICellRenderer` | Render input and output areas of a cell |
| `ILanguageKernel` | Execute code, provide completions, diagnostics, and hover info |
| `IDataFormatter` | Format runtime objects into displayable outputs |
| `IMagicCommand` | Define inline directives like `#!time` |
| `IToolbarAction` | Add buttons to the notebook toolbar or cell menus |
| `ITheme` | Provide a complete visual theme |
| `ILayoutEngine` | Manage spatial arrangement of cells |
| `INotebookSerializer` | Serialize and deserialize notebooks |
| `INotebookPostProcessor` | Transform notebooks after deserialization or before serialization |

### Augmentation Interfaces

These are combined with a primary extension interface to add additional capabilities:

| Interface | Purpose |
|-----------|---------|
| `ICellInteractionHandler` | Handle bidirectional interaction events from rendered cell content |
| `IExtensionSettings` | Expose configurable settings in the UI |

## Installation

```shell
dotnet add package Verso.Abstractions
```

## Usage

```csharp
using Verso.Abstractions;

public class MyExtension : IExtension
{
    public string Id => "my-extension";
    public string Name => "My Extension";
    public string Version => "1.0.0";
}
```

See the [extension authoring guide](https://github.com/DataficationSDK/Verso/blob/main/docs/getting-started.md) for full documentation.
