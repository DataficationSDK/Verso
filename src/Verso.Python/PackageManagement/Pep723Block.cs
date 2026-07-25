namespace Verso.Python.PackageManagement;

/// <summary>
/// What a cell's inline script metadata block declares. Absent when the cell has no block.
/// </summary>
/// <param name="Requirements">Requirement strings from the <c>dependencies</c> array.</param>
/// <param name="RequiresPython">The <c>requires-python</c> specifier, or null when absent.</param>
internal sealed record ScriptMetadata(
    IReadOnlyList<string> Requirements,
    string? RequiresPython);

/// <summary>
/// Reads the inline script metadata block a Python file can carry to declare what it needs:
/// a comment-prefixed TOML fragment opened by <c>#{space}/// script</c> and closed by
/// <c>#{space}///</c>.
/// <para>
/// Only <c>dependencies</c> and <c>requires-python</c> are read. Everything else in the block
/// is ignored, which is why this is a focused reader rather than a TOML parser: two keys do
/// not justify a parsing dependency, and an unrecognized key is not an error.
/// </para>
/// </summary>
internal static class Pep723Block
{
    private const string OpenMarker = "# /// script";
    private const string CloseMarker = "# ///";

    /// <summary>
    /// Read the block from a cell's source. Returns false when there is no block, when it is
    /// never closed, or when it declares neither key. Malformed input reads as absent: a
    /// broken block must not stop the cell from running.
    /// </summary>
    public static bool TryRead(string? code, out ScriptMetadata metadata)
    {
        metadata = new ScriptMetadata(Array.Empty<string>(), null);

        if (string.IsNullOrWhiteSpace(code))
            return false;

        var body = ExtractBody(code!);
        if (body is null)
            return false;

        var requirements = ReadStringArray(body, "dependencies");
        var requiresPython = ReadString(body, "requires-python");

        if (requirements.Count == 0 && requiresPython is null)
            return false;

        metadata = new ScriptMetadata(requirements, requiresPython);
        return true;
    }

    /// <summary>
    /// The block's lines with the comment prefix removed, or null when there is no closed
    /// block. Lines are kept in order so a multi-line array still reads as one.
    /// </summary>
    private static List<string>? ExtractBody(string code)
    {
        var lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var opened = false;
        var body = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (!opened)
            {
                // The opening marker allows extra spacing after the hash, which is how
                // formatters leave it.
                if (IsMarker(trimmed, "///") && trimmed.EndsWith("script", StringComparison.Ordinal))
                    opened = true;
                continue;
            }

            if (IsMarker(trimmed, "///") && trimmed.Length <= CloseMarker.Length + 1)
                return body;

            // The block is defined as contiguous comment lines terminated by the closing marker.
            // Ordinary code before that marker means the block was never closed, so nothing is
            // read: a malformed declaration should install nothing rather than something partial.
            if (!trimmed.StartsWith("#", StringComparison.Ordinal))
                return null;

            body.Add(StripCommentPrefix(trimmed));
        }

        // Never closed. Reading it anyway would let an unterminated block swallow the cell.
        return null;
    }

    private static bool IsMarker(string trimmed, string marker)
    {
        if (!trimmed.StartsWith("#", StringComparison.Ordinal))
            return false;

        var rest = trimmed.Substring(1).TrimStart();
        return rest.StartsWith(marker, StringComparison.Ordinal);
    }

    private static string StripCommentPrefix(string trimmed)
    {
        var rest = trimmed.Substring(1);

        // Exactly one leading space is the convention and is not content; further
        // indentation inside the block is.
        return rest.StartsWith(" ", StringComparison.Ordinal) ? rest.Substring(1) : rest;
    }

    /// <summary>Read a <c>key = "value"</c> entry.</summary>
    private static string? ReadString(List<string> body, string key)
    {
        foreach (var line in body)
        {
            var value = ValueAfterKey(line, key);
            if (value is null)
                continue;

            var quoted = ReadQuoted(value, out _);
            if (quoted is not null)
                return quoted;
        }

        return null;
    }

    /// <summary>
    /// Read a <c>key = [...]</c> entry, whether the array is on one line or spread over
    /// several. Trailing commas and both quote styles are accepted, as TOML allows.
    /// </summary>
    private static IReadOnlyList<string> ReadStringArray(List<string> body, string key)
    {
        var found = new List<string>();

        for (var index = 0; index < body.Count; index++)
        {
            var value = ValueAfterKey(body[index], key);
            if (value is null)
                continue;

            var bracket = value.IndexOf('[');
            if (bracket < 0)
                return found;

            var accumulated = value.Substring(bracket + 1);
            var closed = false;

            while (true)
            {
                var end = accumulated.IndexOf(']');
                if (end >= 0)
                {
                    accumulated = accumulated.Substring(0, end);
                    closed = true;
                    break;
                }

                if (++index >= body.Count)
                    break;

                accumulated += "\n" + body[index];
            }

            // An array that is never closed is not read: a partial list would install
            // something the author did not finish declaring.
            if (closed)
                CollectQuoted(accumulated, found);

            return found;
        }

        return found;
    }

    /// <summary>The text after <c>key =</c> on this line, or null when the line is not it.</summary>
    private static string? ValueAfterKey(string line, string key)
    {
        var text = line.TrimStart();
        if (!text.StartsWith(key, StringComparison.Ordinal))
            return null;

        var rest = text.Substring(key.Length).TrimStart();

        // Guards against "dependencies-extra = ..." matching "dependencies".
        if (!rest.StartsWith("=", StringComparison.Ordinal))
            return null;

        return rest.Substring(1).TrimStart();
    }

    private static void CollectQuoted(string text, List<string> into)
    {
        var position = 0;
        while (position < text.Length)
        {
            var slice = text.Substring(position);
            var value = ReadQuoted(slice, out var consumed);
            if (value is null)
                return;

            if (value.Length > 0)
                into.Add(value);

            position += consumed;
        }
    }

    /// <summary>
    /// Read the first quoted string in the text, reporting how much was consumed. Returns
    /// null when there is no complete quoted string left.
    /// </summary>
    private static string? ReadQuoted(string text, out int consumed)
    {
        consumed = text.Length;

        var open = text.IndexOfAny(new[] { '"', '\'' });
        if (open < 0)
            return null;

        var quote = text[open];
        var close = text.IndexOf(quote, open + 1);
        if (close < 0)
            return null;

        consumed = close + 1;
        return text.Substring(open + 1, close - open - 1).Trim();
    }
}
