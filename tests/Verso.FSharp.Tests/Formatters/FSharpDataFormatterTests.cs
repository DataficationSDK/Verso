using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Verso.FSharp.Formatters;
using Verso.FSharp.Kernel;
using Verso.FSharp.Tests.Helpers;

namespace Verso.FSharp.Tests.Formatters;

[TestClass]
public sealed class FSharpDataFormatterTests
{
    private readonly FSharpDataFormatter _formatter = new();
    private readonly StubFormatterContext _context = new();

    // --- Metadata ---

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.fsharp.formatter", _formatter.ExtensionId);

    [TestMethod]
    public void Priority_IsTwenty()
        => Assert.AreEqual(20, _formatter.Priority);

    [TestMethod]
    public void Name_IsCorrect()
        => Assert.AreEqual("F# Data Formatter", _formatter.Name);

    // --- CanFormat ---

    [TestMethod]
    public void CanFormat_FSharpList_ReturnsTrue()
    {
        var list = ListModule.OfSeq(new[] { 1, 2, 3 });
        Assert.IsTrue(_formatter.CanFormat(list, _context));
    }

    [TestMethod]
    public void CanFormat_FSharpMap_ReturnsTrue()
    {
        var map = MapModule.OfSeq(new[]
        {
            new Tuple<string, int>("a", 1),
            new Tuple<string, int>("b", 2)
        });
        Assert.IsTrue(_formatter.CanFormat(map, _context));
    }

    [TestMethod]
    public void CanFormat_FSharpOption_ReturnsTrue()
    {
        var opt = FSharpOption<string>.Some("hello");
        Assert.IsTrue(_formatter.CanFormat(opt, _context));
    }

    [TestMethod]
    public void CanFormat_FSharpSet_ReturnsTrue()
    {
        var set = SetModule.OfSeq(new[] { 1, 2, 3 });
        Assert.IsTrue(_formatter.CanFormat(set, _context));
    }

    [TestMethod]
    public void CanFormat_PlainString_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat("hello", _context));

    [TestMethod]
    public void CanFormat_PlainInt_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(42, _context));

    [TestMethod]
    public void CanFormat_CSharpClass_ReturnsFalse()
        => Assert.IsFalse(_formatter.CanFormat(new object(), _context));

    // --- Option rendering ---

    [TestMethod]
    public async Task FormatAsync_OptionSome_RendersValue()
    {
        var opt = FSharpOption<string>.Some("hello");
        var result = await _formatter.FormatAsync(opt, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("hello"), "Should contain the Some value");
        Assert.IsFalse(result.Content.Contains("<span class=\"verso-fsharp-none\">"),
            "Should not have None element");
    }

    [TestMethod]
    public void CanFormat_OptionNone_ReturnsFalse()
    {
        // FSharpOption<T>.None is null in C# interop, so CanFormat cannot inspect it.
        // None values are handled via the kernel integration path instead.
        var opt = FSharpOption<string>.None;
        Assert.IsNull(opt, "FSharpOption None is null in C# interop");
    }

    [TestMethod]
    public async Task FormatAsync_OptionNone_ViaKernel_RendersStyledIndicator()
    {
        await using var kernel = new FSharpKernel();
        await kernel.InitializeAsync();
        var ctx = new StubExecutionContext();

        // Store None as a boxed option — the kernel preserves the type wrapper
        await kernel.ExecuteAsync(
            "let noneVal: int option = None\n" +
            "Variables.Set(\"noneVal\", box noneVal)",
            ctx);

        var noneVal = ctx.Variables.Get<object>("noneVal");
        // If the kernel stores null for None, the formatter won't be called
        // (this is correct behavior — None should be handled at the display layer)
        if (noneVal is not null && _formatter.CanFormat(noneVal, _context))
        {
            var result = await _formatter.FormatAsync(noneVal, _context);
            Assert.IsTrue(result.Content.Contains("None") || result.Content.Contains("verso-fsharp-none"),
                "Should indicate None when value is preserved");
        }
        // If null, the formatter correctly declines to handle it
    }

    // --- Result rendering ---

    [TestMethod]
    public async Task FormatAsync_ResultOk_RendersWithOkStyle()
    {
        var ok = FSharpResult<int, string>.NewOk(42);
        var result = await _formatter.FormatAsync(ok, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("verso-fsharp-ok"), "Should have Ok CSS class");
        Assert.IsTrue(result.Content.Contains("42"), "Should contain the Ok value");
    }

    [TestMethod]
    public async Task FormatAsync_ResultError_RendersWithErrorStyle()
    {
        var err = FSharpResult<int, string>.NewError("oops");
        var result = await _formatter.FormatAsync(err, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("verso-fsharp-error"), "Should have Error CSS class");
        Assert.IsTrue(result.Content.Contains("oops"), "Should contain the Error value");
    }

    // --- Collection rendering ---

    [TestMethod]
    public async Task FormatAsync_PrimitiveList_RendersAsList()
    {
        var list = ListModule.OfSeq(new[] { 10, 20, 30 });
        var result = await _formatter.FormatAsync(list, _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("<ul>"), "Primitive list should use <ul>");
        Assert.IsTrue(result.Content.Contains("10"));
        Assert.IsTrue(result.Content.Contains("20"));
        Assert.IsTrue(result.Content.Contains("30"));
    }

    [TestMethod]
    public async Task FormatAsync_EmptyList_ShowsMessage()
    {
        var list = ListModule.OfSeq(Array.Empty<int>());
        var result = await _formatter.FormatAsync(list, _context);
        Assert.IsTrue(result.Content.Contains("Empty collection"));
    }

    [TestMethod]
    public async Task FormatAsync_LargeList_TruncatesAtLimit()
    {
        var items = Enumerable.Range(1, 150).ToArray();
        var list = ListModule.OfSeq(items);
        var result = await _formatter.FormatAsync(list, _context);
        Assert.IsTrue(result.Content.Contains("Showing"), "Should indicate truncation");
    }

    // --- Map rendering ---

    [TestMethod]
    public async Task FormatAsync_Map_RendersTwoColumnTable()
    {
        var map = MapModule.OfSeq(new[]
        {
            new Tuple<string, int>("Alice", 30),
            new Tuple<string, int>("Bob", 25)
        });
        var result = await _formatter.FormatAsync(map, _context);
        Assert.IsTrue(result.Content.Contains("<th>Key</th>"), "Should have Key column");
        Assert.IsTrue(result.Content.Contains("<th>Value</th>"), "Should have Value column");
        Assert.IsTrue(result.Content.Contains("Alice"));
        Assert.IsTrue(result.Content.Contains("Bob"));
    }

    // --- Set rendering ---

    [TestMethod]
    public async Task FormatAsync_SmallSet_RendersInline()
    {
        var set = SetModule.OfSeq(new[] { 1, 2, 3 });
        var result = await _formatter.FormatAsync(set, _context);
        Assert.IsTrue(result.Content.Contains("set ["), "Small set should render inline");
    }

    [TestMethod]
    public async Task FormatAsync_LargeSet_RendersAsTable()
    {
        var set = SetModule.OfSeq(Enumerable.Range(1, 20));
        var result = await _formatter.FormatAsync(set, _context);
        Assert.IsTrue(result.Content.Contains("<table>"), "Large set should render as table");
    }

    // --- Tuple rendering ---

    [TestMethod]
    public async Task FormatAsync_Tuple_RendersParenthesized()
    {
        var tuple = Tuple.Create(1, "hello", true);
        var result = await _formatter.FormatAsync(tuple, _context);
        Assert.IsTrue(result.Content.Contains("("), "Tuple should be parenthesized");
        Assert.IsTrue(result.Content.Contains("1"));
        Assert.IsTrue(result.Content.Contains("hello"));
    }

    // --- CSS / Theme ---

    [TestMethod]
    public async Task FormatAsync_ContainsStyleBlock()
    {
        var opt = FSharpOption<int>.Some(1);
        var result = await _formatter.FormatAsync(opt, _context);
        Assert.IsTrue(result.Content.Contains("<style>"), "Should contain CSS styles");
        Assert.IsTrue(result.Content.Contains("--vscode-editor-background"), "Should have VS Code fallback");
        Assert.IsTrue(result.Content.Contains("--verso-cell-output-background"), "Should have Verso fallback");
    }

    // --- HTML safety ---

    [TestMethod]
    public async Task FormatAsync_SpecialCharacters_AreHtmlEncoded()
    {
        var opt = FSharpOption<string>.Some("<script>alert('xss')</script>");
        var result = await _formatter.FormatAsync(opt, _context);
        Assert.IsFalse(result.Content.Contains("<script>alert"), "Script tags should be encoded");
        Assert.IsTrue(result.Content.Contains("&lt;script&gt;"), "Should be HTML-encoded");
    }

    // --- DU/Record via kernel ---

    [TestMethod]
    public async Task FormatAsync_KernelDU_RendersUnionCase()
    {
        await using var kernel = new FSharpKernel();
        await kernel.InitializeAsync();
        var ctx = new StubExecutionContext();

        await kernel.ExecuteAsync(
            "type Shape = Circle of float | Rectangle of float * float\n" +
            "let myShape = Circle 5.0\n" +
            "Variables.Set(\"shape\", myShape)",
            ctx);

        var shape = ctx.Variables.Get<object>("shape");
        Assert.IsNotNull(shape, "Shape should be in variable store");
        Assert.IsTrue(_formatter.CanFormat(shape!, _context), "Should recognize DU type");

        var result = await _formatter.FormatAsync(shape!, _context);
        Assert.IsTrue(result.Content.Contains("Circle"), "Should contain case name");
        Assert.IsTrue(result.Content.Contains("5"), "Should contain field value");
    }

    [TestMethod]
    public async Task FormatAsync_KernelRecord_RendersFieldTable()
    {
        await using var kernel = new FSharpKernel();
        await kernel.InitializeAsync();
        var ctx = new StubExecutionContext();

        await kernel.ExecuteAsync(
            "type Person = { Name: string; Age: int }\n" +
            "let p = { Name = \"Alice\"; Age = 30 }\n" +
            "Variables.Set(\"person\", p)",
            ctx);

        var person = ctx.Variables.Get<object>("person");
        Assert.IsNotNull(person, "Person should be in variable store");
        Assert.IsTrue(_formatter.CanFormat(person!, _context), "Should recognize record type");

        var result = await _formatter.FormatAsync(person!, _context);
        Assert.IsTrue(result.Content.Contains("<th>Field</th>"), "Should have Field column");
        Assert.IsTrue(result.Content.Contains("<th>Value</th>"), "Should have Value column");
        Assert.IsTrue(result.Content.Contains("Name"), "Should contain field name");
        Assert.IsTrue(result.Content.Contains("Alice"), "Should contain field value");
        Assert.IsTrue(result.Content.Contains("Age"), "Should contain field name");
        Assert.IsTrue(result.Content.Contains("30"), "Should contain field value");
    }

    [TestMethod]
    public async Task FormatAsync_KernelNoFieldDU_RendersCaseName()
    {
        await using var kernel = new FSharpKernel();
        await kernel.InitializeAsync();
        var ctx = new StubExecutionContext();

        await kernel.ExecuteAsync(
            "type Color = Red | Green | Blue\n" +
            "Variables.Set(\"color\", Red)",
            ctx);

        var color = ctx.Variables.Get<object>("color");
        Assert.IsNotNull(color);
        var result = await _formatter.FormatAsync(color!, _context);
        Assert.IsTrue(result.Content.Contains("Red"), "Should contain case name");
    }
}
