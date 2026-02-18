using Verso.Abstractions;
using Verso.Sample.Dice.Models;
using Verso.Testing.Stubs;

namespace Verso.Sample.Dice.Tests;

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
    public async Task Execute_MultipleLines_ReturnsMultipleResults()
    {
        var context = new StubExecutionContext();
        var outputs = await _kernel.ExecuteAsync("1d6\n2d8", context);

        Assert.AreEqual(2, outputs.Count);
        Assert.IsFalse(outputs[0].IsError);
        Assert.IsFalse(outputs[1].IsError);
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
        Assert.IsTrue(info.Content.Contains("max="));
    }

    [TestMethod]
    public async Task GetHoverInfo_InvalidNotation_ReturnsNull()
    {
        var info = await _kernel.GetHoverInfoAsync("abc", 1);
        Assert.IsNull(info);
    }
}
