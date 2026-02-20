# Verso.Testing

Test stubs and fakes for building and testing [Verso](https://github.com/DataficationSDK/Verso) extensions.

## Overview

Provides stub implementations of the Verso engine interfaces so you can unit test your extensions without running a full notebook session. Includes fake execution contexts, variable stores, and notebook metadata.

## Installation

```shell
dotnet add package Verso.Testing
```

## Usage

```csharp
using Verso.Testing;

[TestMethod]
public async Task MyKernel_ExecutesCode()
{
    var context = new StubExecutionContext();
    var kernel = new MyLanguageKernel();

    var result = await kernel.ExecuteAsync("1 + 1", context);

    Assert.IsTrue(result.Success);
}
```

See the [testing extensions guide](https://github.com/DataficationSDK/Verso/blob/main/docs/testing-extensions.md) for full documentation.
