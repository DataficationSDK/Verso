namespace Verso.Sample.Diagram.Models;

/// <summary>
/// Represents a complete diagram graph consisting of nodes and edges.
/// </summary>
/// <param name="Nodes">The set of unique nodes in the graph.</param>
/// <param name="Edges">The set of edges connecting nodes.</param>
public sealed record DiagramGraph(IReadOnlyList<DiagramNode> Nodes, IReadOnlyList<DiagramEdge> Edges);
