using Verso.FSharp.Formatters;
using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Integration;

[TestClass]
public sealed class FSharpFormatterIntegrationTests
{
    private FSharpKernel _kernel = null!;
    private StubExecutionContext _execCtx = null!;
    private FSharpDataFormatter _formatter = null!;
    private StubFormatterContext _fmtCtx = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _kernel = new FSharpKernel();
        await _kernel.InitializeAsync();
        _execCtx = new StubExecutionContext();
        _formatter = new FSharpDataFormatter();
        _fmtCtx = new StubFormatterContext();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task RecordType_DefineAndFormat_VerifyFieldNamesInHtml()
    {
        await _kernel.ExecuteAsync(
            "type Book = { Title: string; Author: string; Year: int }\n" +
            "let book = { Title = \"Dune\"; Author = \"Herbert\"; Year = 1965 }\n" +
            "Variables.Set(\"book\", book)",
            _execCtx);

        var book = _execCtx.Variables.Get<object>("book");
        Assert.IsNotNull(book);

        var result = await _formatter.FormatAsync(book!, _fmtCtx);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("Title"), "Should contain field name Title");
        Assert.IsTrue(result.Content.Contains("Dune"), "Should contain field value Dune");
        Assert.IsTrue(result.Content.Contains("Author"), "Should contain field name Author");
        Assert.IsTrue(result.Content.Contains("Herbert"), "Should contain field value Herbert");
        Assert.IsTrue(result.Content.Contains("1965"), "Should contain field value 1965");
    }

    [TestMethod]
    public async Task DiscriminatedUnion_DefineAndFormat_VerifyCaseNameInOutput()
    {
        await _kernel.ExecuteAsync(
            "type Expr = Num of int | Add of Expr * Expr | Neg of Expr\n" +
            "let expr = Add(Num 1, Neg(Num 2))\n" +
            "Variables.Set(\"expr\", expr)",
            _execCtx);

        var expr = _execCtx.Variables.Get<object>("expr");
        Assert.IsNotNull(expr);
        Assert.IsTrue(_formatter.CanFormat(expr!, _fmtCtx));

        var result = await _formatter.FormatAsync(expr!, _fmtCtx);
        Assert.IsTrue(result.Content.Contains("Add"), "Should contain case name Add");
    }

    [TestMethod]
    public async Task FSharpList_CreateAndFormat_VerifyListRendering()
    {
        await _kernel.ExecuteAsync(
            "let numbers = [1; 2; 3; 4; 5]\n" +
            "Variables.Set(\"numbers\", numbers)",
            _execCtx);

        var numbers = _execCtx.Variables.Get<object>("numbers");
        Assert.IsNotNull(numbers);
        Assert.IsTrue(_formatter.CanFormat(numbers!, _fmtCtx));

        var result = await _formatter.FormatAsync(numbers!, _fmtCtx);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("1"));
        Assert.IsTrue(result.Content.Contains("5"));
    }

    [TestMethod]
    public async Task OptionSomeAndNone_CreateAndFormat_VerifyVisualDistinction()
    {
        await _kernel.ExecuteAsync(
            "let someVal: int option = Some 42\n" +
            "Variables.Set(\"someVal\", someVal)",
            _execCtx);

        var someVal = _execCtx.Variables.Get<object>("someVal");
        Assert.IsNotNull(someVal);

        var someResult = await _formatter.FormatAsync(someVal!, _fmtCtx);
        Assert.IsTrue(someResult.Content.Contains("42"), "Some should show value");
        Assert.IsFalse(someResult.Content.Contains("<span class=\"verso-fsharp-none\">"),
            "Some should not have None element");

        // FSharpOption<T>.None is null in C# interop, so it cannot be formatted.
        // Verify that None stores as null in the variable store (correct behavior).
        await _kernel.ExecuteAsync(
            "let noneVal: int option = None\n" +
            "Variables.Set(\"noneVal\", noneVal)",
            _execCtx);

        var noneVal = _execCtx.Variables.Get<object>("noneVal");
        Assert.IsNull(noneVal, "F# None should be null in C# interop");
    }

    [TestMethod]
    public async Task MultiCellState_BuildUpAndFormat_VerifyFinalValue()
    {
        // Cell 1: Define type
        await _kernel.ExecuteAsync(
            "type Stats = { Count: int; Total: float; Average: float }",
            _execCtx);

        // Cell 2: Create initial data
        await _kernel.ExecuteAsync(
            "let mutable data = [1.0; 2.0; 3.0; 4.0; 5.0]",
            _execCtx);

        // Cell 3: Compute and store
        await _kernel.ExecuteAsync(
            "let stats = { Count = data.Length; Total = List.sum data; Average = List.average data }\n" +
            "Variables.Set(\"stats\", stats)",
            _execCtx);

        var stats = _execCtx.Variables.Get<object>("stats");
        Assert.IsNotNull(stats);

        var result = await _formatter.FormatAsync(stats!, _fmtCtx);
        Assert.IsTrue(result.Content.Contains("Count"), "Should contain Count field");
        Assert.IsTrue(result.Content.Contains("Total"), "Should contain Total field");
        Assert.IsTrue(result.Content.Contains("Average"), "Should contain Average field");
        Assert.IsTrue(result.Content.Contains("5"), "Count should be 5");
        Assert.IsTrue(result.Content.Contains("15"), "Total should be 15");
        Assert.IsTrue(result.Content.Contains("3"), "Average should be 3");
    }

    [TestMethod]
    public async Task VariableStoreRoundTrip_SetAndRetrieveViaFormatter()
    {
        // Set from F# side
        await _kernel.ExecuteAsync(
            "Variables.Set(\"greeting\", \"Hello from F#\")",
            _execCtx);

        // Verify it's in the store
        var greeting = _execCtx.Variables.Get<object>("greeting");
        Assert.IsNotNull(greeting);
        Assert.AreEqual("Hello from F#", greeting!.ToString());

        // Set a typed F# value
        await _kernel.ExecuteAsync(
            "type Point = { X: float; Y: float }\n" +
            "Variables.Set(\"origin\", { X = 0.0; Y = 0.0 })",
            _execCtx);

        var origin = _execCtx.Variables.Get<object>("origin");
        Assert.IsNotNull(origin);
        Assert.IsTrue(_formatter.CanFormat(origin!, _fmtCtx));

        var result = await _formatter.FormatAsync(origin!, _fmtCtx);
        Assert.IsTrue(result.Content.Contains("X"), "Should contain X field");
        Assert.IsTrue(result.Content.Contains("Y"), "Should contain Y field");
    }
}
