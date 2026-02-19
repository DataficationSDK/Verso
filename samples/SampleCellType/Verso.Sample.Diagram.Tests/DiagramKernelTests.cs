using Verso.Abstractions;
using Verso.Sample.Diagram.Models;
using Verso.Testing.Stubs;

namespace Verso.Sample.Diagram.Tests;

[TestClass]
public sealed class DiagramKernelTests
{
    private readonly DiagramKernel _kernel = new();

    [TestMethod]
    public void Metadata_IsCorrect()
    {
        Assert.AreEqual("com.verso.sample.diagram.kernel", _kernel.ExtensionId);
        Assert.AreEqual("diagram", _kernel.LanguageId);
        Assert.AreEqual("Diagram", _kernel.DisplayName);
    }

    [TestMethod]
    public async Task Execute_ValidNotation_ReturnsSvgOutput()
    {
        var context = new StubExecutionContext();
        var outputs = await _kernel.ExecuteAsync("A --> B", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.IsFalse(outputs[0].IsError);
        Assert.AreEqual("image/svg+xml", outputs[0].MimeType);
        Assert.IsTrue(outputs[0].Content.Contains("<svg"));
    }

    [TestMethod]
    public async Task Execute_InvalidNotation_ReturnsError()
    {
        var context = new StubExecutionContext();
        var outputs = await _kernel.ExecuteAsync("not valid syntax", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].IsError);
        Assert.IsTrue(outputs[0].Content.Contains("Syntax error"));
    }

    [TestMethod]
    public async Task Execute_EmptyInput_ReturnsError()
    {
        var context = new StubExecutionContext();
        var outputs = await _kernel.ExecuteAsync("", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].IsError);
    }

    [TestMethod]
    public async Task Execute_CommentsOnly_ReturnsError()
    {
        var context = new StubExecutionContext();
        var outputs = await _kernel.ExecuteAsync("// just a comment", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].IsError);
    }

    [TestMethod]
    public async Task Execute_SetsLastGraphVariable()
    {
        var context = new StubExecutionContext();
        await _kernel.ExecuteAsync("A --> B\nB --> C", context);

        Assert.IsTrue(context.Variables.TryGet<DiagramGraph>("_lastGraph", out var graph));
        Assert.IsNotNull(graph);
        Assert.AreEqual(3, graph!.Nodes.Count);
        Assert.AreEqual(2, graph.Edges.Count);
    }

    [TestMethod]
    public async Task Execute_MultiEdge_ProducesSvgWithAllNodes()
    {
        var context = new StubExecutionContext();
        var outputs = await _kernel.ExecuteAsync("Start --> Process\nProcess --> End", context);

        Assert.AreEqual(1, outputs.Count);
        Assert.IsTrue(outputs[0].Content.Contains("Start"));
        Assert.IsTrue(outputs[0].Content.Contains("Process"));
        Assert.IsTrue(outputs[0].Content.Contains("End"));
    }

    [TestMethod]
    public async Task GetDiagnostics_ValidNotation_ReturnsEmpty()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("A --> B");
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task GetDiagnostics_InvalidNotation_ReturnsError()
    {
        var diagnostics = await _kernel.GetDiagnosticsAsync("bad line");
        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [TestMethod]
    public async Task GetCompletions_ReturnsConnectorSnippets()
    {
        var completions = await _kernel.GetCompletionsAsync("", 0);
        Assert.IsTrue(completions.Count >= 5);
        Assert.IsTrue(completions.Any(c => c.InsertText == "-->"));
        Assert.IsTrue(completions.Any(c => c.InsertText == "==>"));
    }

    [TestMethod]
    public async Task GetHoverInfo_ValidDiagram_ReturnsStats()
    {
        var info = await _kernel.GetHoverInfoAsync("A --> B\nB --> C", 1);
        Assert.IsNotNull(info);
        Assert.IsTrue(info!.Content.Contains("3 nodes"));
        Assert.IsTrue(info.Content.Contains("2 edges"));
    }

    [TestMethod]
    public async Task GetHoverInfo_Empty_ReturnsNull()
    {
        var info = await _kernel.GetHoverInfoAsync("", 0);
        Assert.IsNull(info);
    }
}
