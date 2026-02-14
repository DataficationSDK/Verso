using System.Text;
using Verso.Abstractions;
using Verso.Export;

namespace Verso.Tests.Export;

[TestClass]
public sealed class NotebookMarkdownExporterTests
{
    [TestMethod]
    public void Export_WithTitle_TitleAsHeading()
    {
        var md = ExportToString("My Notebook", Array.Empty<CellModel>());

        Assert.IsTrue(md.StartsWith("# My Notebook"));
    }

    [TestMethod]
    public void Export_NullTitle_NoHeading()
    {
        var md = ExportToString(null, Array.Empty<CellModel>());

        Assert.IsFalse(md.StartsWith("#"));
    }

    [TestMethod]
    public void Export_MarkdownCell_SourceEmittedDirectly()
    {
        var cells = new[]
        {
            new CellModel { Type = "markdown", Source = "## Section\n\nSome text here." }
        };

        var md = ExportToString(null, cells);

        Assert.IsTrue(md.Contains("## Section"));
        Assert.IsTrue(md.Contains("Some text here."));
    }

    [TestMethod]
    public void Export_CodeCell_FencedBlockWithLanguage()
    {
        var cells = new[]
        {
            new CellModel { Type = "code", Language = "python", Source = "print('hello')" }
        };

        var md = ExportToString(null, cells);

        Assert.IsTrue(md.Contains("```python"));
        Assert.IsTrue(md.Contains("print('hello')"));
        Assert.IsTrue(md.Contains("```"));
    }

    [TestMethod]
    public void Export_TextOutput_BlockquotedSection()
    {
        var cells = new[]
        {
            new CellModel
            {
                Type = "code",
                Source = "x",
                Outputs = { new CellOutput("text/plain", "42") }
            }
        };

        var md = ExportToString(null, cells);

        Assert.IsTrue(md.Contains("> Output:"));
        Assert.IsTrue(md.Contains("> 42"));
    }

    [TestMethod]
    public void Export_ErrorOutput_BlockquotedWithName()
    {
        var cells = new[]
        {
            new CellModel
            {
                Type = "code",
                Source = "x",
                Outputs = { new CellOutput("text/plain", "fail", IsError: true, ErrorName: "ValueError") }
            }
        };

        var md = ExportToString(null, cells);

        Assert.IsTrue(md.Contains("> **ValueError:**"));
        Assert.IsTrue(md.Contains("> fail"));
    }

    [TestMethod]
    public void Export_HtmlOutput_BlockquotedWithHtmlLabel()
    {
        var cells = new[]
        {
            new CellModel
            {
                Type = "code",
                Source = "x",
                Outputs = { new CellOutput("text/html", "<b>bold</b>") }
            }
        };

        var md = ExportToString(null, cells);

        Assert.IsTrue(md.Contains("> Output (HTML):"));
        Assert.IsTrue(md.Contains("> <b>bold</b>"));
    }

    [TestMethod]
    public void Export_CodeCell_NullLanguage_FencedBlockWithoutTag()
    {
        var cells = new[]
        {
            new CellModel { Type = "code", Language = null, Source = "some code" }
        };

        var md = ExportToString(null, cells);

        Assert.IsTrue(md.Contains("```\n") || md.Contains("```\r\n"));
        Assert.IsTrue(md.Contains("some code"));
    }

    private static string ExportToString(string? title, IReadOnlyList<CellModel> cells)
    {
        var bytes = NotebookMarkdownExporter.Export(title, cells);
        return Encoding.UTF8.GetString(bytes);
    }
}
