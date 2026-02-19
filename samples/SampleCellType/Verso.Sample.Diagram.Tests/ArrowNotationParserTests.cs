using Verso.Sample.Diagram.Parsing;

namespace Verso.Sample.Diagram.Tests;

[TestClass]
public sealed class ArrowNotationParserTests
{
    [TestMethod]
    public void Parse_SolidArrow_ReturnsEdge()
    {
        var graph = ArrowNotationParser.Parse("A --> B");
        Assert.AreEqual(2, graph.Nodes.Count);
        Assert.AreEqual(1, graph.Edges.Count);
        Assert.AreEqual("-->", graph.Edges[0].ConnectorType);
    }

    [TestMethod]
    public void Parse_SolidLine_ReturnsEdge()
    {
        var graph = ArrowNotationParser.Parse("A --- B");
        Assert.AreEqual(1, graph.Edges.Count);
        Assert.AreEqual("---", graph.Edges[0].ConnectorType);
    }

    [TestMethod]
    public void Parse_Bidirectional_ReturnsEdge()
    {
        var graph = ArrowNotationParser.Parse("A <--> B");
        Assert.AreEqual(1, graph.Edges.Count);
        Assert.AreEqual("<-->", graph.Edges[0].ConnectorType);
    }

    [TestMethod]
    public void Parse_DashedArrow_ReturnsEdge()
    {
        var graph = ArrowNotationParser.Parse("A -.-> B");
        Assert.AreEqual(1, graph.Edges.Count);
        Assert.AreEqual("-.->", graph.Edges[0].ConnectorType);
    }

    [TestMethod]
    public void Parse_ThickArrow_ReturnsEdge()
    {
        var graph = ArrowNotationParser.Parse("A ==> B");
        Assert.AreEqual(1, graph.Edges.Count);
        Assert.AreEqual("==>", graph.Edges[0].ConnectorType);
    }

    [TestMethod]
    public void Parse_LabeledEdge_CapturesLabel()
    {
        var graph = ArrowNotationParser.Parse("Decision --> End : yes");
        Assert.AreEqual(1, graph.Edges.Count);
        Assert.AreEqual("yes", graph.Edges[0].Label);
    }

    [TestMethod]
    public void Parse_UnlabeledEdge_HasNullLabel()
    {
        var graph = ArrowNotationParser.Parse("A --> B");
        Assert.IsNull(graph.Edges[0].Label);
    }

    [TestMethod]
    public void Parse_Comments_AreIgnored()
    {
        var graph = ArrowNotationParser.Parse("// This is a comment\nA --> B");
        Assert.AreEqual(2, graph.Nodes.Count);
        Assert.AreEqual(1, graph.Edges.Count);
    }

    [TestMethod]
    public void Parse_BlankLines_AreIgnored()
    {
        var graph = ArrowNotationParser.Parse("A --> B\n\nB --> C");
        Assert.AreEqual(3, graph.Nodes.Count);
        Assert.AreEqual(2, graph.Edges.Count);
    }

    [TestMethod]
    public void Parse_EmptyInput_ReturnsEmptyGraph()
    {
        var graph = ArrowNotationParser.Parse("");
        Assert.AreEqual(0, graph.Nodes.Count);
        Assert.AreEqual(0, graph.Edges.Count);
    }

    [TestMethod]
    public void Parse_MultipleEdges_CreatesUniqueNodes()
    {
        var graph = ArrowNotationParser.Parse("A --> B\nB --> C\nA --> C");
        Assert.AreEqual(3, graph.Nodes.Count);
        Assert.AreEqual(3, graph.Edges.Count);
    }

    [TestMethod]
    public void Parse_InvalidLine_IsSkipped()
    {
        var graph = ArrowNotationParser.Parse("not a valid line\nA --> B");
        Assert.AreEqual(2, graph.Nodes.Count);
        Assert.AreEqual(1, graph.Edges.Count);
    }

    [TestMethod]
    public void Parse_NodeLabelsMatchIds()
    {
        var graph = ArrowNotationParser.Parse("Start --> End");
        Assert.AreEqual("Start", graph.Nodes[0].Label);
        Assert.AreEqual("End", graph.Nodes[1].Label);
    }

    [TestMethod]
    public void Parse_SourceAndTargetIds_AreCorrect()
    {
        var graph = ArrowNotationParser.Parse("Source --> Target");
        Assert.AreEqual("Source", graph.Edges[0].SourceId);
        Assert.AreEqual("Target", graph.Edges[0].TargetId);
    }

    [TestMethod]
    public void IsValidLine_ValidEdge_ReturnsTrue()
    {
        Assert.IsTrue(ArrowNotationParser.IsValidLine("A --> B"));
        Assert.IsTrue(ArrowNotationParser.IsValidLine("A --- B"));
        Assert.IsTrue(ArrowNotationParser.IsValidLine("A <--> B"));
        Assert.IsTrue(ArrowNotationParser.IsValidLine("A -.-> B"));
        Assert.IsTrue(ArrowNotationParser.IsValidLine("A ==> B"));
        Assert.IsTrue(ArrowNotationParser.IsValidLine("A --> B : label"));
    }

    [TestMethod]
    public void IsValidLine_CommentAndBlank_ReturnsTrue()
    {
        Assert.IsTrue(ArrowNotationParser.IsValidLine("// comment"));
        Assert.IsTrue(ArrowNotationParser.IsValidLine(""));
        Assert.IsTrue(ArrowNotationParser.IsValidLine("   "));
    }

    [TestMethod]
    public void IsValidLine_InvalidSyntax_ReturnsFalse()
    {
        Assert.IsFalse(ArrowNotationParser.IsValidLine("not valid"));
        Assert.IsFalse(ArrowNotationParser.IsValidLine("A -> B"));
    }

    [TestMethod]
    public void Parse_CompleteFlowchart_ParsesCorrectly()
    {
        var code = @"// Sample flowchart
Start --> Process
Process --- End
Decision <--> Both
Maybe -.-> Perhaps
Important ==> Critical
Decision --> End : yes";

        var graph = ArrowNotationParser.Parse(code);
        Assert.AreEqual(9, graph.Nodes.Count);
        Assert.AreEqual(6, graph.Edges.Count);
    }
}
