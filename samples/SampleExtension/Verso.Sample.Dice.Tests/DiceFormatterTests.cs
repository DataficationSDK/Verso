using Verso.Abstractions;
using Verso.Sample.Dice.Models;
using Verso.Testing.Stubs;

namespace Verso.Sample.Dice.Tests;

[TestClass]
public sealed class DiceFormatterTests
{
    private readonly DiceFormatter _formatter = new();

    [TestMethod]
    public void Metadata_IsCorrect()
    {
        Assert.AreEqual("com.verso.sample.dice.formatter", _formatter.ExtensionId);
        Assert.AreEqual(10, _formatter.Priority);
        Assert.IsTrue(_formatter.SupportedTypes.Contains(typeof(DiceResult)));
    }

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

    [TestMethod]
    public async Task Format_MaxRoll_HasBoldGreenStyle()
    {
        var notation = DiceNotation.TryParse("1d6")!;
        var result = new DiceResult(notation, new[] { 6 });
        var context = new StubFormatterContext();

        var output = await _formatter.FormatAsync(result, context);

        Assert.IsTrue(output.Content.Contains("color:#2e7d32"));
    }

    [TestMethod]
    public async Task Format_MinRoll_HasBoldRedStyle()
    {
        var notation = DiceNotation.TryParse("1d20")!;
        var result = new DiceResult(notation, new[] { 1 });
        var context = new StubFormatterContext();

        var output = await _formatter.FormatAsync(result, context);

        Assert.IsTrue(output.Content.Contains("color:#c62828"));
    }
}
