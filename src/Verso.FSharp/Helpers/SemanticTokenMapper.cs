using FSharp.Compiler.EditorServices;
using Verso.Abstractions;

namespace Verso.FSharp.Helpers;

/// <summary>
/// Maps the F# compiler's semantic classifications to Verso semantic tokens.
/// </summary>
internal static class SemanticTokenMapper
{
    private static readonly IReadOnlyList<string> Mutable = new[] { SemanticTokenModifiers.Mutable };

    /// <summary>
    /// Converts the classifications of a virtual document into tokens of <paramref name="cellCode"/>,
    /// the cell that begins after <paramref name="prefixLineCount"/> lines of it. Classifications in
    /// the prefix, and any that span lines, are dropped.
    /// </summary>
    /// <remarks>
    /// The compiler classifies a long identifier by its last part over the whole of its range, so
    /// <c>List.map</c> arrives as one function and <c>List</c> as a module beneath it. Each range is
    /// cut back to its last identifier, which leaves <c>List</c> to its own classification. Where one
    /// span is still classified more than once the last classification is kept, and a span that
    /// overlaps the one before it is dropped.
    /// </remarks>
    public static IReadOnlyList<SemanticToken> MapClassifications(
        IEnumerable<SemanticClassificationItem> items, int prefixLineCount, string cellCode)
    {
        var lines = cellCode.Split('\n');
        var bySpan = new Dictionary<(int Line, int Column, int Length), SemanticToken>();
        foreach (var item in items)
        {
            var range = item.Range;
            if (range.StartLine != range.EndLine) continue;

            // FCS lines are 1-based; cell lines are 0-based.
            var line = range.StartLine - 1 - prefixLineCount;
            if (line < 0 || line >= lines.Length) continue;

            var lineText = lines[line].TrimEnd('\r');
            var start = range.StartColumn;
            var end = Math.Min(range.EndColumn, lineText.Length);
            if (end <= start) continue;

            var mapped = Map(item.Type);
            if (mapped is null) continue;

            start = LastIdentifierStart(lineText, start, end);

            var (type, modifiers) = mapped.Value;
            bySpan[(line, start, end - start)] = new SemanticToken(line, start, end - start, type, modifiers);
        }

        var tokens = new List<SemanticToken>(bySpan.Count);
        foreach (var token in bySpan.Values.OrderBy(t => t.Line).ThenBy(t => t.StartColumn))
        {
            if (tokens.Count > 0 && tokens[^1] is var previous
                && previous.Line == token.Line && previous.StartColumn + previous.Length > token.StartColumn)
            {
                continue;
            }
            tokens.Add(token);
        }

        return tokens;
    }

    // The start of the identifier after the last '.' of a dotted name in [start, end), or start.
    // A ``quoted identifier`` may hold a '.' of its own and is taken whole.
    private static int LastIdentifierStart(string lineText, int start, int end)
    {
        var span = lineText.AsSpan(start, end - start);
        if (span.EndsWith("``"))
        {
            var open = span[..^2].LastIndexOf("``");
            if (open <= 0) return start;
            return span[open - 1] == '.' ? start + open : start;
        }

        var dot = span.LastIndexOf('.');
        return dot >= 0 && dot < span.Length - 1 ? start + dot + 1 : start;
    }

    /// <summary>
    /// Returns the token type and modifiers for a classification, or <c>null</c> for one the
    /// editor already colors from the source text alone (a format specifier inside a string) or
    /// that carries no meaning of its own.
    /// </summary>
    internal static (string Type, IReadOnlyList<string>? Modifiers)? Map(SemanticClassificationType type) => type switch
    {
        SemanticClassificationType.Namespace => (SemanticTokenTypes.Namespace, null),
        SemanticClassificationType.Module => (SemanticTokenTypes.Namespace, null),

        SemanticClassificationType.ReferenceType => (SemanticTokenTypes.Class, null),
        SemanticClassificationType.ConstructorForReferenceType => (SemanticTokenTypes.Class, null),
        SemanticClassificationType.DisposableType => (SemanticTokenTypes.Class, null),
        SemanticClassificationType.Exception => (SemanticTokenTypes.Class, null),
        SemanticClassificationType.ValueType => (SemanticTokenTypes.Struct, null),
        SemanticClassificationType.ConstructorForValueType => (SemanticTokenTypes.Struct, null),
        SemanticClassificationType.Enumeration => (SemanticTokenTypes.Enum, null),
        SemanticClassificationType.Interface => (SemanticTokenTypes.Interface, null),
        SemanticClassificationType.Delegate => (SemanticTokenTypes.Type, null),
        SemanticClassificationType.Type => (SemanticTokenTypes.Type, null),
        SemanticClassificationType.TypeDef => (SemanticTokenTypes.Type, null),
        SemanticClassificationType.TypeArgument => (SemanticTokenTypes.TypeParameter, null),

        SemanticClassificationType.UnionCase => (SemanticTokenTypes.EnumMember, null),
        SemanticClassificationType.Literal => (SemanticTokenTypes.EnumMember, null),

        SemanticClassificationType.Function => (SemanticTokenTypes.Function, null),
        SemanticClassificationType.IntrinsicFunction => (SemanticTokenTypes.Function, null),
        SemanticClassificationType.Method => (SemanticTokenTypes.Method, null),
        SemanticClassificationType.ExtensionMethod => (SemanticTokenTypes.Method, null),

        SemanticClassificationType.Property => (SemanticTokenTypes.Property, null),
        SemanticClassificationType.RecordField => (SemanticTokenTypes.Property, null),
        SemanticClassificationType.RecordFieldAsFunction => (SemanticTokenTypes.Property, null),
        SemanticClassificationType.UnionCaseField => (SemanticTokenTypes.Property, null),
        SemanticClassificationType.Field => (SemanticTokenTypes.Property, null),
        SemanticClassificationType.MutableRecordField => (SemanticTokenTypes.Property, Mutable),
        SemanticClassificationType.Event => (SemanticTokenTypes.Event, null),

        SemanticClassificationType.Value => (SemanticTokenTypes.Variable, null),
        SemanticClassificationType.LocalValue => (SemanticTokenTypes.Variable, null),
        SemanticClassificationType.DisposableTopLevelValue => (SemanticTokenTypes.Variable, null),
        SemanticClassificationType.DisposableLocalValue => (SemanticTokenTypes.Variable, null),
        SemanticClassificationType.MutableVar => (SemanticTokenTypes.Variable, Mutable),
        SemanticClassificationType.NamedArgument => (SemanticTokenTypes.Parameter, null),

        SemanticClassificationType.ComputationExpression => (SemanticTokenTypes.Keyword, null),
        SemanticClassificationType.Operator => (SemanticTokenTypes.Operator, null),

        // Printf (a %d inside a format string) and Plaintext.
        _ => null,
    };
}
