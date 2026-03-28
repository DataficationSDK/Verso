using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Cli.Execution;
using Verso.Execution;

namespace Verso.Cli.Tests.Execution;

[TestClass]
public class OutputRendererTests
{
    private StringWriter _stdout = null!;
    private StringWriter _stderr = null!;

    [TestInitialize]
    public void Setup()
    {
        _stdout = new StringWriter();
        _stderr = new StringWriter();
    }

    [TestMethod]
    public void RenderCell_TextPlain_WritesToStdout()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var cell = CreateCodeCell("csharp", new CellOutput("text/plain", "Hello, World!"));
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        var output = _stdout.ToString();
        Assert.IsTrue(output.Contains("Hello, World!"));
        Assert.IsTrue(output.Contains("Cell 0"));
        Assert.IsTrue(output.Contains("csharp"));
    }

    [TestMethod]
    public void RenderCell_TextHtml_StripsTagsAndWritesToStdout()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var cell = CreateCodeCell("csharp", new CellOutput("text/html", "<b>Bold</b> text"));
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        var output = _stdout.ToString();
        Assert.IsTrue(output.Contains("Bold text"));
        Assert.IsFalse(output.Contains("<b>"));
    }

    [TestMethod]
    public void RenderCell_ApplicationJson_PrettyPrints()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var cell = CreateCodeCell("csharp", new CellOutput("application/json", "{\"key\":\"value\"}"));
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        var output = _stdout.ToString();
        Assert.IsTrue(output.Contains("\"key\""));
        Assert.IsTrue(output.Contains("\"value\""));
    }

    [TestMethod]
    public void RenderCell_TextXError_WritesToStderr()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var cell = CreateCodeCell("csharp", new CellOutput("text/x-error", "Something went wrong"));
        var result = ExecutionResult.Failed(cell.Id, 1, TimeSpan.FromSeconds(1), new Exception("fail"));

        renderer.RenderCell(0, cell, result);

        var stderr = _stderr.ToString();
        Assert.IsTrue(stderr.Contains("Something went wrong"));
    }

    [TestMethod]
    public void RenderCell_TextMarkdown_SkippedByDefault()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var cell = CreateCodeCell("csharp", new CellOutput("text/markdown", "# Header"));
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        var output = _stdout.ToString();
        Assert.IsFalse(output.Contains("# Header"));
    }

    [TestMethod]
    public void RenderCell_ImagePng_SkippedInTextMode()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var cell = CreateCodeCell("csharp", new CellOutput("image/png", "base64data"));
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        var output = _stdout.ToString();
        Assert.IsFalse(output.Contains("base64data"));
    }

    [TestMethod]
    public void RenderCell_NonCodeCell_Skipped()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var cell = new CellModel
        {
            Type = "markdown",
            Language = null,
            Source = "# Some heading",
            Outputs = { new CellOutput("text/plain", "rendered markdown") }
        };
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        var output = _stdout.ToString();
        Assert.AreEqual("", output);
    }

    [TestMethod]
    public void WriteSummary_ShowsCorrectCounts()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var results = new List<ExecutionResult>
        {
            ExecutionResult.Success(Guid.NewGuid(), 1, TimeSpan.FromSeconds(1)),
            ExecutionResult.Success(Guid.NewGuid(), 2, TimeSpan.FromSeconds(2)),
            ExecutionResult.Failed(Guid.NewGuid(), 3, TimeSpan.FromSeconds(1), new Exception("fail"))
        };

        renderer.WriteSummary(results, TimeSpan.FromSeconds(4));

        var output = _stdout.ToString();
        Assert.IsTrue(output.Contains("3 total"));
        Assert.IsTrue(output.Contains("2 succeeded"));
        Assert.IsTrue(output.Contains("1 failed"));
        Assert.IsTrue(output.Contains("4.0s"));
    }

    [TestMethod]
    public void WriteProgress_Verbose_WritesToStderr()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: true);
        renderer.WriteProgress(1, 3, 1, "csharp", "Cell 1 completed in 0.5s (Success)");

        var stderr = _stderr.ToString();
        Assert.IsTrue(stderr.Contains("[1/3]"));
        Assert.IsTrue(stderr.Contains("Cell 1 completed"));
    }

    [TestMethod]
    public void WriteProgress_NotVerbose_WritesNothing()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        renderer.WriteProgress(1, 3, 1, "csharp", "Cell 1 completed in 0.5s (Success)");

        Assert.AreEqual("", _stderr.ToString());
    }

    [TestMethod]
    public void RenderCell_TextMarkdown_IncludedWhenFlagSet()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false, includeMarkdown: true);
        var cell = CreateCodeCell("csharp", new CellOutput("text/markdown", "# Header"));
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        var output = _stdout.ToString();
        Assert.IsTrue(output.Contains("# Header"));
    }

    [TestMethod]
    public void RenderCell_MarkdownCell_SkippedByDefault()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var cell = new CellModel
        {
            Type = "markdown",
            Source = "# Some heading"
        };
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        Assert.AreEqual("", _stdout.ToString());
    }

    [TestMethod]
    public void RenderCell_MarkdownCell_IncludedWhenFlagSet()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false, includeMarkdown: true);
        var cell = new CellModel
        {
            Type = "markdown",
            Source = "# Some heading"
        };
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        var output = _stdout.ToString();
        Assert.IsTrue(output.Contains("# Some heading"));
        Assert.IsTrue(output.Contains("Cell 0"));
        Assert.IsTrue(output.Contains("markdown"));
    }

    [TestMethod]
    public void RenderCell_HtmlCell_IncludedWhenFlagSet()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false, includeMarkdown: true);
        var cell = new CellModel
        {
            Type = "html",
            Source = "<h1>Title</h1>"
        };
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        var output = _stdout.ToString();
        Assert.IsTrue(output.Contains("Title"));
        Assert.IsFalse(output.Contains("<h1>"));
    }

    [TestMethod]
    public void RenderCell_HtmlCell_SkippedByDefault()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var cell = new CellModel
        {
            Type = "html",
            Source = "<h1>Title</h1>"
        };
        var result = ExecutionResult.Success(cell.Id, 1, TimeSpan.FromSeconds(1));

        renderer.RenderCell(0, cell, result);

        Assert.AreEqual("", _stdout.ToString());
    }

    [TestMethod]
    public void RenderCell_ParametersCell_SkippedByDefault()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false);
        var cell = new CellModel { Type = "parameters", Source = "" };
        var result = ExecutionResult.Success(cell.Id, 0, TimeSpan.Zero);
        var parameters = new Dictionary<string, object>
        {
            ["startDate"] = "2026-01-01",
            ["region"] = "ALL"
        };

        renderer.RenderCell(0, cell, result, parameters);

        Assert.AreEqual("", _stdout.ToString());
    }

    [TestMethod]
    public void RenderCell_ParametersCell_RenderedWhenFlagSet()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false, showParameters: true);
        var cell = new CellModel { Type = "parameters", Source = "" };
        var result = ExecutionResult.Success(cell.Id, 0, TimeSpan.Zero);
        var parameters = new Dictionary<string, object>
        {
            ["startDate"] = "2026-01-01",
            ["region"] = "ALL"
        };

        renderer.RenderCell(0, cell, result, parameters);

        var output = _stdout.ToString();
        Assert.IsTrue(output.Contains("parameters"));
        Assert.IsTrue(output.Contains("startDate"));
        Assert.IsTrue(output.Contains("2026-01-01"));
        Assert.IsTrue(output.Contains("region"));
        Assert.IsTrue(output.Contains("ALL"));
    }

    [TestMethod]
    public void RenderCell_ParametersCell_EmptyParameters_ShowsPlaceholder()
    {
        var renderer = new OutputRenderer(_stdout, _stderr, verbose: false, showParameters: true);
        var cell = new CellModel { Type = "parameters", Source = "" };
        var result = ExecutionResult.Success(cell.Id, 0, TimeSpan.Zero);

        renderer.RenderCell(0, cell, result);

        var output = _stdout.ToString();
        Assert.IsTrue(output.Contains("no parameters"));
    }

    private static CellModel CreateCodeCell(string language, params CellOutput[] outputs)
    {
        var cell = new CellModel
        {
            Type = "code",
            Language = language,
            Source = "// test"
        };
        cell.Outputs.AddRange(outputs);
        return cell;
    }
}
