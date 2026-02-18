# Testing Extensions

The `Verso.Testing` library provides stub contexts and fake implementations for unit testing Verso extensions without running the full host. This guide covers setup, the available test doubles, and testing patterns for each extension interface type.

## Setup

### Adding the NuGet Reference

Add `Verso.Testing` (and a test framework) to your test project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.5.2" />
    <PackageReference Include="MSTest.TestFramework" Version="3.5.2" />
    <PackageReference Include="coverlet.collector" Version="6.0.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyExtension\MyExtension.csproj" />
    <PackageReference Include="Verso.Testing" Version="0.5.0" />
  </ItemGroup>
</Project>
```

If developing against local source, use a `ProjectReference` to `Verso.Testing.csproj` instead.

### Required Using Directives

```csharp
using Verso.Abstractions;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;  // if using fakes
```

## Available Test Doubles

### Stubs (Verso.Testing.Stubs)

Stubs implement context interfaces with sensible defaults and track calls for assertion.

| Stub | Implements | Key Tracking Properties |
|---|---|---|
| `StubVersoContext` | `IVersoContext` | `WrittenOutputs` |
| `StubExecutionContext` | `IExecutionContext` | `WrittenOutputs`, `DisplayedOutputs`, `CellId`, `ExecutionCount` |
| `StubFormatterContext` | `IFormatterContext` | `MimeType`, `MaxWidth`, `MaxHeight` |
| `StubCellRenderContext` | `ICellRenderContext` | `CellId`, `CellMetadata`, `Dimensions`, `IsSelected` |
| `StubMagicCommandContext` | `IMagicCommandContext` | `WrittenOutputs`, `RemainingCode`, `SuppressExecution` |
| `StubToolbarActionContext` | `IToolbarActionContext` | `WrittenOutputs`, `DownloadedFiles`, `SelectedCellIds`, `NotebookCells`, `ActiveKernelId` |
| `StubNotebookOperations` | `INotebookOperations` | `ExecutedCellIds`, `ExecuteAllCallCount`, `ClearedOutputCellIds`, `InsertedCells`, `RemovedCellIds`, `MovedCells`, `ExecutedCodeCalls`, `RestartedKernelIds` |

All stubs provide:
- A real `VariableStore` instance via the `Variables` property
- A `StubThemeContext` via the `Theme` property
- A `StubNotebookOperations` via the `Notebook` property
- `CancellationToken.None` by default (settable)

### Fakes (Verso.Testing.Fakes)

Fakes implement extension interfaces with configurable behavior, useful for testing code that interacts with extensions.

| Fake | Implements | Description |
|---|---|---|
| `FakeExtension` | `IExtension` | Bare extension with lifecycle tracking (`OnLoadedCallCount`, `OnUnloadedCallCount`). |
| `FakeLanguageKernel` | `ILanguageKernel` | Configurable kernel with injectable `executeFunc`. Tracks `InitializeCallCount`, `DisposeCallCount`. |
| `FakeCellRenderer` | `ICellRenderer` | Returns simple `text/plain` results. Tracks lifecycle calls. |
| `FakeDataFormatter` | `IDataFormatter` | Formats strings to `text/plain`. Tracks lifecycle calls. |

---

## Testing Each Interface Type

### Testing a Language Kernel

Use `StubExecutionContext` to test `ExecuteAsync`, and call the language service methods directly (they do not require a context).

```csharp
[TestClass]
public sealed class DiceKernelTests
{
    private readonly DiceExtension _kernel = new();

    [TestMethod]
    public void Metadata_IsCorrect()
    {
        Assert.AreEqual("com.verso.sample.dice", _kernel.ExtensionId);
        Assert.AreEqual("dice", _kernel.LanguageId);
        Assert.AreEqual("Dice", _kernel.DisplayName);
    }

    [TestMethod]
    public async Task Execute_ValidNotation_ReturnsResult()
    {
        var context = new StubExecutionContext();
        var outputs = await _kernel.ExecuteAsync("1d6", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.IsFalse(outputs[0].IsError);
        Assert.IsTrue(outputs[0].Content.StartsWith("1d6 =>"));
    }

    [TestMethod]
    public async Task Execute_InvalidNotation_ReturnsError()
    {
        var context = new StubExecutionContext();
        var outputs = await _kernel.ExecuteAsync("invalid", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].IsError);
        Assert.IsTrue(outputs[0].Content.Contains("Invalid dice notation"));
    }

    [TestMethod]
    public async Task Execute_SetsLastRollVariable()
    {
        var context = new StubExecutionContext();
        await _kernel.ExecuteAsync("2d6", context);

        Assert.IsTrue(context.Variables.TryGet<DiceResult>("_lastRoll", out var result));
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result!.Rolls.Count);
    }

    [TestMethod]
    public async Task GetDiagnostics_ValidNotation_ReturnsEmpty()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("2d6");
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task GetDiagnostics_InvalidNotation_ReturnsError()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("bad");
        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [TestMethod]
    public async Task GetCompletions_ReturnsSnippets()
    {
        var completions = await _kernel.GetCompletionsAsync("", 0);
        Assert.IsTrue(completions.Count > 0);
        Assert.IsTrue(completions.Any(c => c.InsertText == "1d20"));
    }

    [TestMethod]
    public async Task GetHoverInfo_ValidNotation_ReturnsStats()
    {
        var info = await _kernel.GetHoverInfoAsync("2d6", 1);
        Assert.IsNotNull(info);
        Assert.IsTrue(info!.Content.Contains("min="));
    }
}
```

### Testing a Cell Renderer

Use `StubCellRenderContext` to verify rendered HTML output. Configure `IsSelected`, `Dimensions`, and `CellMetadata` as needed.

```csharp
[TestClass]
public sealed class DiceRendererTests
{
    private readonly DiceRenderer _renderer = new();

    [TestMethod]
    public async Task RenderInput_EscapesHtml()
    {
        var context = new StubCellRenderContext();
        var result = await _renderer.RenderInputAsync("<script>alert(1)</script>", context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsFalse(result.Content.Contains("<script>"));
    }

    [TestMethod]
    public async Task RenderOutput_Error_ShowsErrorStyling()
    {
        var context = new StubCellRenderContext();
        var errorOutput = new CellOutput("text/plain", "Something went wrong", IsError: true);

        var result = await _renderer.RenderOutputAsync(errorOutput, context);

        Assert.IsTrue(result.Content.Contains("color:#d32f2f"));
    }

    [TestMethod]
    public async Task RenderInput_RespectsSelectedState()
    {
        var context = new StubCellRenderContext { IsSelected = true };
        var result = await _renderer.RenderInputAsync("1d20", context);

        // Verify the renderer produces valid output when selected
        Assert.AreEqual("text/html", result.MimeType);
    }
}
```

### Testing a Data Formatter

Use `StubFormatterContext` to test `CanFormat` and `FormatAsync`. Adjust `MimeType`, `MaxWidth`, and `MaxHeight` to test different formatting scenarios.

```csharp
[TestClass]
public sealed class DiceFormatterTests
{
    private readonly DiceFormatter _formatter = new();

    [TestMethod]
    public void CanFormat_DiceResult_ReturnsTrue()
    {
        var notation = DiceNotation.TryParse("2d6")!;
        var result = new DiceResult(notation, new[] { 3, 5 });
        var context = new StubFormatterContext();

        Assert.IsTrue(_formatter.CanFormat(result, context));
    }

    [TestMethod]
    public void CanFormat_String_ReturnsFalse()
    {
        var context = new StubFormatterContext();
        Assert.IsFalse(_formatter.CanFormat("hello", context));
    }

    [TestMethod]
    public async Task Format_DiceResult_ReturnsHtmlTable()
    {
        var notation = DiceNotation.TryParse("2d6")!;
        var result = new DiceResult(notation, new[] { 3, 5 });
        var context = new StubFormatterContext();

        var output = await _formatter.FormatAsync(result, context);

        Assert.AreEqual("text/html", output.MimeType);
        Assert.IsTrue(output.Content.Contains("<table"));
        Assert.IsTrue(output.Content.Contains("Total: 8"));
    }

    [TestMethod]
    public async Task Format_WithModifier_ShowsModifierColumn()
    {
        var notation = DiceNotation.TryParse("1d20+5")!;
        var result = new DiceResult(notation, new[] { 15 });
        var context = new StubFormatterContext();

        var output = await _formatter.FormatAsync(result, context);

        Assert.IsTrue(output.Content.Contains("+5"));
        Assert.IsTrue(output.Content.Contains("Total: 20"));
    }
}
```

### Testing a Toolbar Action

Use `StubToolbarActionContext` to configure notebook state and verify behavior. Set `NotebookCells` to control which cells exist, and check `StubNotebookOperations` for executed operations.

```csharp
[TestClass]
public sealed class DiceRollActionTests
{
    private readonly DiceRollAction _action = new();

    [TestMethod]
    public async Task IsEnabled_NoDiceCells_ReturnsFalse()
    {
        var context = new StubToolbarActionContext
        {
            NotebookCells = new[]
            {
                new CellModel { Language = "csharp", Source = "Console.WriteLine();" }
            }
        };

        Assert.IsFalse(await _action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task IsEnabled_WithDiceCells_ReturnsTrue()
    {
        var context = new StubToolbarActionContext
        {
            NotebookCells = new[]
            {
                new CellModel { Language = "dice", Source = "2d6" }
            }
        };

        Assert.IsTrue(await _action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task Execute_RunsAllDiceCells()
    {
        var diceCell1 = new CellModel { Language = "dice", Source = "1d20" };
        var csharpCell = new CellModel { Language = "csharp", Source = "var x = 1;" };
        var diceCell2 = new CellModel { Language = "dice", Source = "2d6" };

        var notebookOps = new StubNotebookOperations();
        var context = new StubToolbarActionContext
        {
            NotebookCells = new[] { diceCell1, csharpCell, diceCell2 },
            Notebook = notebookOps
        };

        await _action.ExecuteAsync(context);

        // Verify only dice cells were executed
        Assert.AreEqual(2, notebookOps.ExecutedCellIds.Count);
        CollectionAssert.Contains(notebookOps.ExecutedCellIds, diceCell1.Id);
        CollectionAssert.Contains(notebookOps.ExecutedCellIds, diceCell2.Id);
    }
}
```

### Testing a Magic Command

Use `StubMagicCommandContext` to test command execution. Check `WrittenOutputs` for output and `SuppressExecution` for execution control.

```csharp
[TestClass]
public sealed class MyMagicCommandTests
{
    [TestMethod]
    public async Task Execute_WritesOutput()
    {
        var command = new MyMagicCommand();
        var context = new StubMagicCommandContext
        {
            RemainingCode = "some code after the directive"
        };

        await command.ExecuteAsync("arg1 arg2", context);

        Assert.IsTrue(context.WrittenOutputs.Count > 0);
    }

    [TestMethod]
    public async Task Execute_SuppressesKernelExecution()
    {
        var command = new RestartCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.IsTrue(context.SuppressExecution);
    }
}
```

---

## Testing with StubNotebookOperations

`StubNotebookOperations` tracks every notebook mutation for assertion. This is valuable when testing toolbar actions, magic commands, or any code that interacts with `context.Notebook`.

```csharp
var ops = new StubNotebookOperations();

// After your code calls context.Notebook.ExecuteCellAsync(id):
Assert.AreEqual(1, ops.ExecutedCellIds.Count);

// After calling context.Notebook.RestartKernelAsync("csharp"):
CollectionAssert.Contains(ops.RestartedKernelIds, "csharp");

// After calling context.Notebook.InsertCellAsync(0, "code", "dice"):
Assert.AreEqual(1, ops.InsertedCells.Count);
Assert.AreEqual("dice", ops.InsertedCells[0].Language);

// After calling context.Notebook.ClearAllOutputsAsync():
Assert.AreEqual(1, ops.ClearAllOutputsCallCount);
```

## Testing with Cancellation

All stub contexts expose a settable `CancellationToken`. Use this to verify your extension handles cancellation correctly:

```csharp
[TestMethod]
public async Task Execute_RespectsCanncellation()
{
    var cts = new CancellationTokenSource();
    cts.Cancel();

    var context = new StubExecutionContext
    {
        CancellationToken = cts.Token
    };

    await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
    {
        await myKernel.ExecuteAsync("long running code", context);
    });
}
```

## Testing with Variables

All stubs include a real `VariableStore`. Set variables before calling your extension, then verify them afterward:

```csharp
[TestMethod]
public async Task Execute_ReadsAndWritesVariables()
{
    var context = new StubExecutionContext();
    context.Variables.Set("input", 42);

    await myKernel.ExecuteAsync("use input", context);

    Assert.IsTrue(context.Variables.TryGet<string>("output", out var result));
    Assert.IsNotNull(result);
}
```

## Using FakeLanguageKernel

When testing code that depends on a kernel (e.g., a cell type or integration test), inject a `FakeLanguageKernel` with custom behavior:

```csharp
var fakeKernel = new FakeLanguageKernel(
    languageId: "test",
    displayName: "Test",
    executeFunc: (code, ctx) =>
    {
        var output = new CellOutput("text/plain", $"Result: {code}");
        return Task.FromResult<IReadOnlyList<CellOutput>>(new[] { output });
    });

var outputs = await fakeKernel.ExecuteAsync("hello", new StubExecutionContext());
Assert.AreEqual("Result: hello", outputs[0].Content);
Assert.AreEqual(0, fakeKernel.InitializeCallCount); // Not yet initialized
```

---

## Complete Example

The `Verso.Sample.Dice.Tests` project at `samples/SampleExtension/Verso.Sample.Dice.Tests/` provides a complete working example with tests for:

- Kernel execution, diagnostics, completions, and hover info (`DiceKernelTests.cs`)
- Formatter type checking and HTML output (`DiceFormatterTests.cs`)
- Dice notation parsing (`DiceNotationTests.cs`)

Run the full test suite:

```bash
dotnet test samples/SampleExtension/Verso.Sample.Dice.Tests/
```

---

## See Also

- [Extension Interfaces](extension-interfaces.md) -- the interfaces you are testing
- [Context Reference](context-reference.md) -- details on what each context provides
- [Best Practices](best-practices.md) -- error handling and thread safety patterns
