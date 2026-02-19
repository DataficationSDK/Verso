using System.Text.RegularExpressions;
using Verso.Sample.Diagram.Models;

namespace Verso.Sample.Diagram.Parsing;

/// <summary>
/// Parses arrow notation text into a <see cref="DiagramGraph"/>.
/// </summary>
/// <remarks>
/// Supported syntax:
/// <code>
/// Start --> Process           // solid arrow
/// Process --- End             // solid line, no arrow
/// Decision &lt;--> Both       // bidirectional
/// Maybe -.-> Perhaps          // dashed arrow
/// Important ==> Critical      // thick arrow
/// Decision --> End : yes      // labeled edge
/// // This is a comment
/// </code>
/// </remarks>
public static class ArrowNotationParser
{
    private static readonly Regex EdgePattern = new(
        @"^(\w+)\s*(-->|---|<-->|-.->|==>)\s*(\w+)(?:\s*:\s*(.+))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses the arrow notation text and returns a <see cref="DiagramGraph"/>.
    /// </summary>
    /// <param name="text">The arrow notation source text.</param>
    /// <returns>A parsed diagram graph.</returns>
    public static DiagramGraph Parse(string text)
    {
        var nodes = new Dictionary<string, DiagramNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<DiagramEdge>();

        var lines = text.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            var match = EdgePattern.Match(line);
            if (!match.Success)
                continue;

            var sourceId = match.Groups[1].Value;
            var connector = match.Groups[2].Value;
            var targetId = match.Groups[3].Value;
            var label = match.Groups[4].Success ? match.Groups[4].Value.Trim() : null;

            if (!nodes.ContainsKey(sourceId))
                nodes[sourceId] = new DiagramNode(sourceId, sourceId);
            if (!nodes.ContainsKey(targetId))
                nodes[targetId] = new DiagramNode(targetId, targetId);

            edges.Add(new DiagramEdge(sourceId, targetId, connector, label));
        }

        return new DiagramGraph(nodes.Values.ToList(), edges);
    }

    /// <summary>
    /// Validates a single line of arrow notation.
    /// </summary>
    /// <returns>True if the line is a valid edge, comment, or blank line.</returns>
    public static bool IsValidLine(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
            return true;
        return EdgePattern.IsMatch(trimmed);
    }
}
