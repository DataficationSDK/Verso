using Verso.Abstractions;
using Verso.Testing.Stubs;

namespace Verso.Sample.Dice.Tests;

[TestClass]
public sealed class DiceRendererTests
{
    private readonly DiceRenderer _renderer = new();

    [TestMethod]
    public void Metadata_IsCorrect()
    {
        Assert.AreEqual("com.verso.sample.dice.renderer", _renderer.ExtensionId);
        Assert.AreEqual("dice", _renderer.CellTypeId);
        Assert.IsFalse(_renderer.CollapsesInputOnExecute);
    }

    [TestMethod]
    public async Task RenderInput_ReturnsHtml()
    {
        var context = new StubCellRenderContext();
        var result = await _renderer.RenderInputAsync("2d6+3", context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("2d6+3"));
        Assert.IsTrue(result.Content.Contains("<pre"));
    }

    [TestMethod]
    public async Task RenderInput_EscapesHtml()
    {
        var context = new StubCellRenderContext();
        var result = await _renderer.RenderInputAsync("<script>alert(1)</script>", context);

        Assert.IsFalse(result.Content.Contains("<script>"));
    }

    [TestMethod]
    public async Task RenderOutput_NormalOutput_ReturnsHtml()
    {
        var context = new StubCellRenderContext();
        var output = new CellOutput("text/plain", "2d6 => [3, 5] = 8");
        var result = await _renderer.RenderOutputAsync(output, context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("2d6"));
    }

    [TestMethod]
    public async Task RenderOutput_ErrorOutput_ShowsErrorStyle()
    {
        var context = new StubCellRenderContext();
        var output = new CellOutput("text/plain", "Invalid dice notation", IsError: true);
        var result = await _renderer.RenderOutputAsync(output, context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("color:#d32f2f"));
    }

    [TestMethod]
    public void GetEditorLanguage_ReturnsNull()
    {
        Assert.IsNull(_renderer.GetEditorLanguage());
    }
}
