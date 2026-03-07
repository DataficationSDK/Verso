# Verso.Testing

Test stubs and fakes for building and testing [Verso](https://github.com/DataficationSDK/Verso) extensions.

## Overview

Provides stub and fake implementations of the Verso engine interfaces so you can unit test your extensions without running a full notebook session. All stubs accumulate outputs in list properties for easy assertion.

### Stubs

| Class | Implements | Description |
|-------|-----------|-------------|
| `StubExecutionContext` | `IExecutionContext` | Kernel execution testing with `WrittenOutputs`, `DisplayedOutputs`, `UpdatedOutputs` |
| `StubMagicCommandContext` | `IMagicCommandContext` | Magic command testing with `RemainingCode` and `SuppressExecution` |
| `StubFormatterContext` | `IFormatterContext` | Data formatter testing with configurable dimensions and MIME type |
| `StubCellRenderContext` | `ICellRenderContext` | Cell renderer testing with configurable cell metadata and selection state |
| `StubToolbarActionContext` | `IToolbarActionContext` | Toolbar action testing with `SelectedCellIds` and `DownloadedFiles` |
| `StubVersoContext` | `IVersoContext` | Base context for extensions needing only `IVersoContext` |
| `StubNotebookOperations` | `INotebookOperations` | Records all notebook operations for assertion (`ExecutedCellIds`, `InsertedCells`, `RemovedCellIds`, etc.) |

### Fakes

| Class | Implements | Description |
|-------|-----------|-------------|
| `FakeExtension` | `IExtension` | Bare extension with settable properties and load/unload tracking |
| `FakeLanguageKernel` | `ILanguageKernel` | Configurable kernel with injectable execution behavior |
| `FakeDataFormatter` | `IDataFormatter` | String formatter with load/unload tracking |
| `FakeCellRenderer` | `ICellRenderer` | Pass-through renderer with load/unload tracking |
| `FakeCellInteractionHandler` | `IDataFormatter`, `ICellInteractionHandler` | Dual-interface fake for testing bidirectional cell interactions |

## Installation

```shell
dotnet add package Verso.Testing
```

## Usage

```csharp
using Verso.Testing.Stubs;

[TestMethod]
public async Task MyKernel_ExecutesCode()
{
    var context = new StubExecutionContext();
    var kernel = new MyLanguageKernel();

    var outputs = await kernel.ExecuteAsync("1 + 1", context);

    Assert.AreEqual(1, outputs.Count);
    Assert.AreEqual("2", outputs[0].Content);
}
```

See the [testing extensions guide](https://github.com/DataficationSDK/Verso/blob/main/docs/testing-extensions.md) for full documentation.
