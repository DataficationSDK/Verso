using Verso.Abstractions;
using Verso.Extensions.Renderers;
using Verso.Tests.Helpers;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class MarkdownRendererTests
{
    private readonly MarkdownRenderer _renderer = new();
    private readonly StubCellRenderContext _context = new();

    [TestMethod]
    public void ExtensionId_IsCorrect()
        => Assert.AreEqual("verso.renderer.markdown", _renderer.ExtensionId);

    [TestMethod]
    public void CellTypeId_IsMarkdown()
        => Assert.AreEqual("markdown", _renderer.CellTypeId);

    [TestMethod]
    public void GetEditorLanguage_ReturnsMarkdown()
        => Assert.AreEqual("markdown", _renderer.GetEditorLanguage());

    [TestMethod]
    public async Task RenderInput_H1_ProducesH1Tag()
    {
        var result = await _renderer.RenderInputAsync("# Heading 1", _context);
        Assert.AreEqual("text/html", result.MimeType);
        Assert.IsTrue(result.Content.Contains("<h1"), "Expected <h1 tag");
        Assert.IsTrue(result.Content.Contains("Heading 1"));
    }

    [TestMethod]
    public async Task RenderInput_H2_ProducesH2Tag()
    {
        var result = await _renderer.RenderInputAsync("## Heading 2", _context);
        Assert.IsTrue(result.Content.Contains("<h2"), "Expected <h2 tag");
    }

    [TestMethod]
    public async Task RenderInput_H3_ProducesH3Tag()
    {
        var result = await _renderer.RenderInputAsync("### Heading 3", _context);
        Assert.IsTrue(result.Content.Contains("<h3"), "Expected <h3 tag");
    }

    [TestMethod]
    public async Task RenderInput_H4_ProducesH4Tag()
    {
        var result = await _renderer.RenderInputAsync("#### Heading 4", _context);
        Assert.IsTrue(result.Content.Contains("<h4"), "Expected <h4 tag");
    }

    [TestMethod]
    public async Task RenderInput_H5_ProducesH5Tag()
    {
        var result = await _renderer.RenderInputAsync("##### Heading 5", _context);
        Assert.IsTrue(result.Content.Contains("<h5"), "Expected <h5 tag");
    }

    [TestMethod]
    public async Task RenderInput_H6_ProducesH6Tag()
    {
        var result = await _renderer.RenderInputAsync("###### Heading 6", _context);
        Assert.IsTrue(result.Content.Contains("<h6"), "Expected <h6 tag");
    }

    [TestMethod]
    public async Task RenderInput_CodeFence_ProducesCodeBlock()
    {
        var result = await _renderer.RenderInputAsync("```csharp\nvar x = 1;\n```", _context);
        Assert.IsTrue(result.Content.Contains("<code"));
        Assert.IsTrue(result.Content.Contains("var x = 1;"));
    }

    [TestMethod]
    public async Task RenderInput_Table_ProducesTableTag()
    {
        var md = "| A | B |\n|---|---|\n| 1 | 2 |";
        var result = await _renderer.RenderInputAsync(md, _context);
        Assert.IsTrue(result.Content.Contains("<table"));
        Assert.IsTrue(result.Content.Contains("<th"));
    }

    [TestMethod]
    public async Task RenderInput_TaskList_ProducesCheckbox()
    {
        var md = "- [x] Done\n- [ ] Pending";
        var result = await _renderer.RenderInputAsync(md, _context);
        Assert.IsTrue(result.Content.Contains("type=\"checkbox\""));
    }

    [TestMethod]
    public async Task RenderInput_Bold_ProducesStrongTag()
    {
        var result = await _renderer.RenderInputAsync("**bold text**", _context);
        Assert.IsTrue(result.Content.Contains("<strong>"));
    }

    [TestMethod]
    public async Task RenderInput_Italic_ProducesEmTag()
    {
        var result = await _renderer.RenderInputAsync("*italic text*", _context);
        Assert.IsTrue(result.Content.Contains("<em>"));
    }

    [TestMethod]
    public async Task RenderInput_Link_ProducesAnchorTag()
    {
        var result = await _renderer.RenderInputAsync("[link](http://example.com)", _context);
        Assert.IsTrue(result.Content.Contains("<a "));
        Assert.IsTrue(result.Content.Contains("http://example.com"));
    }

    [TestMethod]
    public async Task RenderInput_Image_ProducesImgTag()
    {
        var result = await _renderer.RenderInputAsync("![alt](image.png)", _context);
        Assert.IsTrue(result.Content.Contains("<img "));
    }

    [TestMethod]
    public async Task RenderInput_EmptyInput_ReturnsEmptyString()
    {
        var result = await _renderer.RenderInputAsync("", _context);
        Assert.AreEqual(string.Empty, result.Content);
    }

    [TestMethod]
    public async Task RenderInput_NullInput_ReturnsEmptyString()
    {
        var result = await _renderer.RenderInputAsync(null!, _context);
        Assert.AreEqual(string.Empty, result.Content);
    }

    [TestMethod]
    public async Task RenderOutput_PassesThrough()
    {
        var output = new CellOutput("text/plain", "hello world");
        var result = await _renderer.RenderOutputAsync(output, _context);
        Assert.AreEqual("text/plain", result.MimeType);
        Assert.AreEqual("hello world", result.Content);
    }

    [TestMethod]
    public void CollapsesInputOnExecute_IsTrue()
        => Assert.IsTrue(_renderer.CollapsesInputOnExecute);
}
