using System.Text.Json.Nodes;
using Verso.Abstractions;
using Verso.Python.Helpers;

namespace Verso.Python.Host;

/// <summary>
/// Turns completion, hover, and diagnostic replies into the models the editors consume. A reply
/// that is missing or malformed yields nothing rather than an error: editor assistance is
/// advisory, and a bad frame must not surface as a failure in the notebook.
/// </summary>
internal static class HostIntelliSense
{
    public static IReadOnlyList<Completion> MapCompletions(JsonObject? reply)
    {
        if (reply?[HostProtocol.CompletionsField] is not JsonArray items || items.Count == 0)
            return Array.Empty<Completion>();

        var results = new List<Completion>(items.Count);
        foreach (var node in items)
        {
            if (node is not JsonObject item)
                continue;

            var name = HostProtocol.TryGetString(item, HostProtocol.NameField);
            if (string.IsNullOrEmpty(name))
                continue;

            var insert = HostProtocol.TryGetString(item, HostProtocol.InsertField);
            var description = HostProtocol.TryGetString(item, HostProtocol.DocField);

            results.Add(new Completion(
                DisplayText: name!,
                InsertText: string.IsNullOrEmpty(insert) ? name! : insert!,
                Kind: JediTypeMapper.Map(HostProtocol.TryGetString(item, HostProtocol.KindField) ?? ""),
                Description: string.IsNullOrEmpty(description) ? null : description));
        }

        return results;
    }

    public static IReadOnlyList<Diagnostic> MapDiagnostics(JsonObject? reply)
    {
        if (reply?[HostProtocol.DiagnosticsField] is not JsonArray items || items.Count == 0)
            return Array.Empty<Diagnostic>();

        var results = new List<Diagnostic>(items.Count);
        foreach (var node in items)
        {
            if (node is not JsonObject item)
                continue;

            var message = HostProtocol.TryGetString(item, HostProtocol.MessageField);
            if (string.IsNullOrEmpty(message))
                continue;

            // Positions arrive cell-relative and zero-based on both axes, which is the shape the
            // editors use, so there is no arithmetic to do here.
            var line = Math.Max(HostProtocol.TryGetInt(item, HostProtocol.LineField) ?? 0, 0);
            var column = Math.Max(HostProtocol.TryGetInt(item, HostProtocol.ColumnField) ?? 0, 0);
            var endLine = Math.Max(HostProtocol.TryGetInt(item, HostProtocol.EndLineField) ?? line, line);
            var endColumn = Math.Max(
                HostProtocol.TryGetInt(item, HostProtocol.EndColumnField) ?? column + 1, 0);

            results.Add(new Diagnostic(
                Severity: DiagnosticSeverity.Error,
                Message: message!,
                StartLine: line,
                StartColumn: column,
                EndLine: endLine,
                EndColumn: endColumn,
                Code: HostProtocol.TryGetString(item, HostProtocol.DiagnosticCodeField)));
        }

        return results;
    }

    /// <summary>
    /// Build the hover model from a reply. The range is computed here rather than in the
    /// subprocess: finding the identifier around a cursor is text work that needs neither the
    /// interpreter nor the namespace.
    /// </summary>
    public static HoverInfo? MapHover(JsonObject? reply, string code, int cursorPosition)
    {
        if (reply?[HostProtocol.HoverField] is not JsonObject hover)
            return null;

        var content = HostProtocol.TryGetString(hover, HostProtocol.ContentField);
        if (string.IsNullOrEmpty(content))
            return null;

        var (line, _) = PythonPositionHelpers.OffsetToLineColumn(code, cursorPosition);
        var range = PythonPositionHelpers.ComputeIdentifierRange(code, cursorPosition, line);

        return new HoverInfo(content!, Range: range);
    }
}
