using System.Net;
using System.Text;
using Verso.Sample.Diagram.Models;

namespace Verso.Sample.Diagram.Rendering;

/// <summary>
/// Renders a <see cref="DiagramGraph"/> as an SVG flowchart with a top-to-bottom layered layout.
/// </summary>
public static class SvgFlowchartRenderer
{
    private const int NodeWidth = 120;
    private const int NodeHeight = 40;
    private const int HorizontalGap = 40;
    private const int VerticalGap = 60;
    private const int Padding = 20;

    /// <summary>
    /// Converts a diagram graph to an SVG string.
    /// </summary>
    public static string Render(DiagramGraph graph)
    {
        if (graph.Nodes.Count == 0)
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"40\"><text x=\"10\" y=\"25\" font-size=\"14\">Empty diagram</text></svg>";

        // Assign layers using BFS from root nodes
        var layers = AssignLayers(graph);
        var positions = CalculatePositions(layers);

        // Calculate SVG dimensions
        var maxX = positions.Values.Max(p => p.X) + NodeWidth + Padding;
        var maxY = positions.Values.Max(p => p.Y) + NodeHeight + Padding;
        var svgWidth = (int)maxX + Padding;
        var svgHeight = (int)maxY + Padding;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{svgWidth}\" height=\"{svgHeight}\">");

        // Arrow markers
        sb.Append("<defs>");
        sb.Append("<marker id=\"arrowhead\" markerWidth=\"10\" markerHeight=\"7\" refX=\"10\" refY=\"3.5\" orient=\"auto\">");
        sb.Append("<polygon points=\"0 0, 10 3.5, 0 7\" fill=\"#333\" />");
        sb.Append("</marker>");
        sb.Append("<marker id=\"arrowhead-reverse\" markerWidth=\"10\" markerHeight=\"7\" refX=\"0\" refY=\"3.5\" orient=\"auto\">");
        sb.Append("<polygon points=\"10 0, 0 3.5, 10 7\" fill=\"#333\" />");
        sb.Append("</marker>");
        sb.Append("</defs>");

        // Render edges
        foreach (var edge in graph.Edges)
        {
            if (!positions.TryGetValue(edge.SourceId, out var srcPos) ||
                !positions.TryGetValue(edge.TargetId, out var tgtPos))
                continue;

            var x1 = srcPos.X + NodeWidth / 2.0;
            var y1 = srcPos.Y + NodeHeight;
            var x2 = tgtPos.X + NodeWidth / 2.0;
            var y2 = tgtPos.Y;

            var (strokeStyle, strokeWidth, markerEnd, markerStart) = GetEdgeStyle(edge.ConnectorType);

            sb.Append($"<line x1=\"{x1}\" y1=\"{y1}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"#333\" stroke-width=\"{strokeWidth}\"{strokeStyle}{markerEnd}{markerStart} />");

            // Edge label
            if (!string.IsNullOrEmpty(edge.Label))
            {
                var labelX = (x1 + x2) / 2;
                var labelY = (y1 + y2) / 2 - 5;
                sb.Append($"<text x=\"{labelX}\" y=\"{labelY}\" text-anchor=\"middle\" font-size=\"11\" fill=\"#666\">{WebUtility.HtmlEncode(edge.Label)}</text>");
            }
        }

        // Render nodes
        foreach (var node in graph.Nodes)
        {
            if (!positions.TryGetValue(node.Id, out var pos))
                continue;

            sb.Append($"<rect x=\"{pos.X}\" y=\"{pos.Y}\" width=\"{NodeWidth}\" height=\"{NodeHeight}\" rx=\"6\" ry=\"6\" fill=\"#E8F4FD\" stroke=\"#4A90D9\" stroke-width=\"1.5\" />");
            sb.Append($"<text x=\"{pos.X + NodeWidth / 2.0}\" y=\"{pos.Y + NodeHeight / 2.0 + 5}\" text-anchor=\"middle\" font-size=\"13\" fill=\"#333\">{WebUtility.HtmlEncode(node.Label)}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static Dictionary<string, List<string>> AssignLayers(DiagramGraph graph)
    {
        var layers = new Dictionary<string, int>();
        var targets = new HashSet<string>(graph.Edges.Select(e => e.TargetId), StringComparer.OrdinalIgnoreCase);
        var roots = graph.Nodes
            .Where(n => !targets.Contains(n.Id))
            .Select(n => n.Id)
            .ToList();

        // If no roots found (cycle), use first node
        if (roots.Count == 0 && graph.Nodes.Count > 0)
            roots.Add(graph.Nodes[0].Id);

        // BFS
        var queue = new Queue<string>();
        foreach (var root in roots)
        {
            layers[root] = 0;
            queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentLayer = layers[current];

            foreach (var edge in graph.Edges.Where(e =>
                string.Equals(e.SourceId, current, StringComparison.OrdinalIgnoreCase)))
            {
                if (!layers.ContainsKey(edge.TargetId) || layers[edge.TargetId] < currentLayer + 1)
                {
                    layers[edge.TargetId] = currentLayer + 1;
                    queue.Enqueue(edge.TargetId);
                }
            }
        }

        // Assign any unvisited nodes to layer 0
        foreach (var node in graph.Nodes)
        {
            if (!layers.ContainsKey(node.Id))
                layers[node.Id] = 0;
        }

        // Group by layer
        var grouped = new Dictionary<string, List<string>>();
        foreach (var (nodeId, layer) in layers)
        {
            var key = layer.ToString();
            if (!grouped.ContainsKey(key))
                grouped[key] = new List<string>();
            grouped[key].Add(nodeId);
        }

        return grouped;
    }

    private static Dictionary<string, (double X, double Y)> CalculatePositions(Dictionary<string, List<string>> layers)
    {
        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
        var sortedLayers = layers.OrderBy(l => int.Parse(l.Key)).ToList();

        for (int i = 0; i < sortedLayers.Count; i++)
        {
            var nodesInLayer = sortedLayers[i].Value;
            var totalWidth = nodesInLayer.Count * NodeWidth + (nodesInLayer.Count - 1) * HorizontalGap;
            var startX = Padding + (totalWidth > 0 ? 0 : 0);

            for (int j = 0; j < nodesInLayer.Count; j++)
            {
                var x = Padding + j * (NodeWidth + HorizontalGap);
                var y = Padding + i * (NodeHeight + VerticalGap);
                positions[nodesInLayer[j]] = (x, y);
            }
        }

        return positions;
    }

    private static (string StrokeStyle, int StrokeWidth, string MarkerEnd, string MarkerStart) GetEdgeStyle(string connectorType)
    {
        return connectorType switch
        {
            "-->" => ("", 1, " marker-end=\"url(#arrowhead)\"", ""),
            "---" => ("", 1, "", ""),
            "<-->" => ("", 1, " marker-end=\"url(#arrowhead)\"", " marker-start=\"url(#arrowhead-reverse)\""),
            "-.>" or ".->" or "-.->" => (" stroke-dasharray=\"5,3\"", 1, " marker-end=\"url(#arrowhead)\"", ""),
            "==>" => ("", 3, " marker-end=\"url(#arrowhead)\"", ""),
            _ => ("", 1, " marker-end=\"url(#arrowhead)\"", "")
        };
    }
}
