namespace Verso.Sample.Diagram.Models;

/// <summary>
/// Represents a node in a diagram graph.
/// </summary>
/// <param name="Id">Unique identifier for the node (used in arrow notation).</param>
/// <param name="Label">Display label for the node. Defaults to the Id if not specified.</param>
public sealed record DiagramNode(string Id, string Label);
