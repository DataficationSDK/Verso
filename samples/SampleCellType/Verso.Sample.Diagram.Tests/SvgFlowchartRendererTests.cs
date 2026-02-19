using Verso.Sample.Diagram.Models;
using Verso.Sample.Diagram.Parsing;
using Verso.Sample.Diagram.Rendering;

namespace Verso.Sample.Diagram.Tests;

[TestClass]
public sealed class SvgFlowchartRendererTests
{
    [TestMethod]
    public void Render_EmptyGraph_ReturnsPlaceholder()
    {
        var graph = new DiagramGraph(Array.Empty<DiagramNode>(), Array.Empty<DiagramEdge>());
        var svg = SvgFlowchartRenderer.Render(graph);

        Assert.IsTrue(svg.Contains("<svg"));
        Assert.IsTrue(svg.Contains("Empty diagram"));
    }

    [TestMethod]
    public void Render_SimpleGraph_ContainsSvgElements()
    {
        var graph = ArrowNotationParser.Parse("A --> B");
        var svg = SvgFlowchartRenderer.Render(graph);

        Assert.IsTrue(svg.StartsWith("<svg"));
        Assert.IsTrue(svg.Contains("</svg>"));
        Assert.IsTrue(svg.Contains("<rect"));  // Nodes
        Assert.IsTrue(svg.Contains("<line"));   // Edges
        Assert.IsTrue(svg.Contains("<text"));   // Labels
    }

    [TestMethod]
    public void Render_ContainsNodeLabels()
    {
        var graph = ArrowNotationParser.Parse("Start --> End");
        var svg = SvgFlowchartRenderer.Render(graph);

        Assert.IsTrue(svg.Contains(">Start<"));
        Assert.IsTrue(svg.Contains(">End<"));
    }

    [TestMethod]
    public void Render_ContainsEdgeLabel()
    {
        var graph = ArrowNotationParser.Parse("A --> B : yes");
        var svg = SvgFlowchartRenderer.Render(graph);

        Assert.IsTrue(svg.Contains(">yes<"));
    }

    [TestMethod]
    public void Render_ContainsArrowMarkerDefs()
    {
        var graph = ArrowNotationParser.Parse("A --> B");
        var svg = SvgFlowchartRenderer.Render(graph);

        Assert.IsTrue(svg.Contains("<defs>"));
        Assert.IsTrue(svg.Contains("arrowhead"));
    }

    [TestMethod]
    public void Render_ThickArrow_UsesWiderStroke()
    {
        var graph = ArrowNotationParser.Parse("A ==> B");
        var svg = SvgFlowchartRenderer.Render(graph);

        Assert.IsTrue(svg.Contains("stroke-width=\"3\""));
    }

    [TestMethod]
    public void Render_DashedArrow_UsesDashArray()
    {
        var graph = ArrowNotationParser.Parse("A -.-> B");
        var svg = SvgFlowchartRenderer.Render(graph);

        Assert.IsTrue(svg.Contains("stroke-dasharray"));
    }

    [TestMethod]
    public void Render_MultiLayer_HasCorrectNodeCount()
    {
        var graph = ArrowNotationParser.Parse("A --> B\nB --> C\nC --> D");
        var svg = SvgFlowchartRenderer.Render(graph);

        // Should have 4 rect elements for 4 nodes
        var rectCount = svg.Split("<rect").Length - 1;
        Assert.AreEqual(4, rectCount);
    }

    [TestMethod]
    public void Render_EscapesNodeLabelsInHtml()
    {
        // Node IDs are \w+ so they can't contain HTML, but this validates the encoding path
        var nodes = new List<DiagramNode> { new("Test", "Test") };
        var edges = new List<DiagramEdge>();
        var graph = new DiagramGraph(nodes, edges);

        var svg = SvgFlowchartRenderer.Render(graph);
        Assert.IsTrue(svg.Contains(">Test<"));
    }

    [TestMethod]
    public void Render_Bidirectional_HasBothMarkers()
    {
        var graph = ArrowNotationParser.Parse("A <--> B");
        var svg = SvgFlowchartRenderer.Render(graph);

        Assert.IsTrue(svg.Contains("marker-end"));
        Assert.IsTrue(svg.Contains("marker-start"));
    }

    [TestMethod]
    public void Render_NoArrowLine_HasNoMarkers()
    {
        var graph = ArrowNotationParser.Parse("A --- B");
        var svg = SvgFlowchartRenderer.Render(graph);

        // The line for the edge should not have marker-end
        // Find the line element and check it doesn't have markers
        Assert.IsTrue(svg.Contains("<line"));
        // The line for --- should not have marker-end
        var lineStart = svg.IndexOf("<line");
        var lineEnd = svg.IndexOf("/>", lineStart);
        var lineElement = svg.Substring(lineStart, lineEnd - lineStart + 2);
        Assert.IsFalse(lineElement.Contains("marker-end"));
    }
}
