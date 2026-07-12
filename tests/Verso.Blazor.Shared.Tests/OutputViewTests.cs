using Bunit;
using Verso.Abstractions;
using Verso.Blazor.Shared.Components.Notebook;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class OutputViewTests : BunitTestContext
{
    private IRenderedComponent<OutputView> Render(IReadOnlyList<CellOutput> outputs, bool extended = true)
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        return TestContext.RenderComponent<OutputView>(parameters => parameters
            .Add(p => p.Outputs, outputs)
            .Add(p => p.ExtendedMimeTypes, extended));
    }

    [TestMethod]
    public void RendersPlainText()
    {
        var cut = Render(new[] { CellOutput.Plain("hello world") });

        var text = cut.Find(".verso-output--text pre");
        Assert.AreEqual("hello world", text.TextContent);
    }

    [TestMethod]
    public void RendersError_WithNameAndStackTrace()
    {
        var cut = Render(new[] { CellOutput.Error("boom", "InvalidOperationException", "at Foo.Bar()") });

        Assert.AreEqual("InvalidOperationException", cut.Find(".verso-output-error-name").TextContent);
        Assert.AreEqual("boom", cut.Find(".verso-output-error-content").TextContent);
        Assert.AreEqual("at Foo.Bar()", cut.Find(".verso-output-error-stack").TextContent);
    }

    [TestMethod]
    public void RendersHtmlOutput_AsMarkup()
    {
        var cut = Render(new[] { CellOutput.Html("<b>bold</b>") });

        var html = cut.Find(".verso-output--html");
        Assert.IsNotNull(html.QuerySelector("b"));
    }

    [TestMethod]
    public void RendersImage_AsDataUri()
    {
        var cut = Render(new[] { CellOutput.Png("QUJD") });

        var img = cut.Find(".verso-output--html img");
        StringAssert.StartsWith(img.GetAttribute("src"), "data:image/png;base64,QUJD");
    }

    [TestMethod]
    public void RendersJson_AsCollapsibleTree()
    {
        var cut = Render(new[] { CellOutput.Json("{\"key\":\"value\"}") });

        Assert.IsNotNull(cut.Find(".verso-json"));
        StringAssert.Contains(cut.Markup, "value");
    }

    [TestMethod]
    public void RendersCsv_AsTable()
    {
        var cut = Render(new[] { CellOutput.Csv("a,b\n1,2") });

        var table = cut.Find("table.verso-csv");
        Assert.AreEqual(2, table.QuerySelectorAll("tr").Length);
        Assert.AreEqual(2, table.QuerySelectorAll("th").Length);
    }

    [TestMethod]
    public void RendersMermaid_InMermaidContainer()
    {
        var cut = Render(new[] { CellOutput.Mermaid("graph TD; A-->B;") });

        Assert.IsNotNull(cut.Find(".verso-mermaid-container pre.mermaid"));
    }

    [TestMethod]
    public void ExtendedMimeTypesFalse_JsonFallsBackToPlainPre()
    {
        var cut = Render(new[] { CellOutput.Json("{\"key\":\"value\"}") }, extended: false);

        Assert.AreEqual(0, cut.FindAll(".verso-json").Count);
        Assert.IsNotNull(cut.Find(".verso-output--text pre"));
    }

    [TestMethod]
    public void ExtendedMimeTypesFalse_MermaidFallsBackToPlainPre()
    {
        var cut = Render(new[] { CellOutput.Mermaid("graph TD; A-->B;") }, extended: false);

        Assert.AreEqual(0, cut.FindAll(".verso-mermaid-container").Count);
        Assert.IsNotNull(cut.Find(".verso-output--text pre"));
    }

    [TestMethod]
    public void ExtendedMimeTypesFalse_ErrorOmitsStackTrace()
    {
        var cut = Render(new[] { CellOutput.Error("boom", "Error", "at Foo.Bar()") }, extended: false);

        Assert.AreEqual(0, cut.FindAll(".verso-output-error-stack").Count);
        Assert.AreEqual("boom", cut.Find(".verso-output-error-content").TextContent);
    }

    [TestMethod]
    public void ExtendedMimeTypesFalse_HtmlStillRendersAsMarkup()
    {
        var cut = Render(new[] { CellOutput.Html("<i>italic</i>") }, extended: false);

        Assert.IsNotNull(cut.Find(".verso-output--html i"));
    }

    [TestMethod]
    public void RendersMultipleOutputs_InOrder()
    {
        var cut = Render(new[] { CellOutput.Plain("first"), CellOutput.Html("<b>second</b>") });

        var outputs = cut.FindAll(".verso-output");
        Assert.AreEqual(2, outputs.Count);
        StringAssert.Contains(outputs[0].TextContent, "first");
        StringAssert.Contains(outputs[1].TextContent, "second");
    }
}
