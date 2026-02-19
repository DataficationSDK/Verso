using Verso.Abstractions;
using Verso.Testing.Stubs;

namespace Verso.Sample.Diagram.Tests;

[TestClass]
public sealed class DiagramRendererTests
{
    private readonly DiagramRenderer _renderer = new();

    [TestMethod]
    public void Metadata_IsCorrect()
    {
        Assert.AreEqual("com.verso.sample.diagram.renderer", _renderer.ExtensionId);
        Assert.AreEqual("diagram", _renderer.CellTypeId);
        Assert.IsTrue(_renderer.CollapsesInputOnExecute);
    }

    [TestMethod]
    public async Task RenderInput_ReturnsHtml()
    {
        var context = new StubCellRenderContext();
        var result = await _renderer.RenderInputAsync("A --> B", context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("A --&gt; B"));
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
    public async Task RenderOutput_SvgOutput_WrapsInContainer()
    {
        var context = new StubCellRenderContext();
        var svgContent = "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect/></svg>";
        var output = new CellOutput("image/svg+xml", svgContent);
        var result = await _renderer.RenderOutputAsync(output, context);

        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("verso-diagram-output"));
        Assert.IsTrue(result.Content.Contains(svgContent));
    }

    [TestMethod]
    public async Task RenderOutput_ErrorOutput_ShowsErrorStyle()
    {
        var context = new StubCellRenderContext();
        var output = new CellOutput("text/plain", "Syntax error on line 1", IsError: true);
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
