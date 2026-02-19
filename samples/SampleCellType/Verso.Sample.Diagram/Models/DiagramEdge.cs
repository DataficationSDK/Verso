namespace Verso.Sample.Diagram.Models;

/// <summary>
/// Represents a directed or undirected edge between two nodes.
/// </summary>
/// <param name="SourceId">The identifier of the source node.</param>
/// <param name="TargetId">The identifier of the target node.</param>
/// <param name="ConnectorType">The connector type string (e.g. "-->", "---", "&lt;-->", "-.->", "==>" ).</param>
/// <param name="Label">Optional label displayed on the edge.</param>
public sealed record DiagramEdge(string SourceId, string TargetId, string ConnectorType, string? Label);
