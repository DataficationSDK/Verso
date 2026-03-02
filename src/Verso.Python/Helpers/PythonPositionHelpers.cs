namespace Verso.Python.Helpers;

/// <summary>
/// Pure helper methods for offset/position math and cross-cell source building.
/// </summary>
internal static class PythonPositionHelpers
{
    /// <summary>
    /// Converts a 0-based character offset within text to a (line, column) pair, both 0-based.
    /// </summary>
    internal static (int Line, int Column) OffsetToLineColumn(string text, int offset)
    {
        if (offset <= 0)
            return (0, 0);

        var line = 0;
        var col = 0;
        var len = Math.Min(offset, text.Length);

        for (var i = 0; i < len; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                col = 0;
            }
            else
            {
                col++;
            }
        }

        return (line, col);
    }

    /// <summary>
    /// Finds the identifier (or dotted name) at the cursor and returns its 0-based range.
    /// </summary>
    internal static (int StartLine, int StartColumn, int EndLine, int EndColumn)? ComputeIdentifierRange(
        string code, int cursorPosition, int line)
    {
        var pos = Math.Min(cursorPosition, code.Length);

        // Walk backwards to find identifier start
        var start = pos;
        while (start > 0 && IsIdentifierChar(code[start - 1]))
            start--;

        // Walk forwards to find identifier end
        var end = pos;
        while (end < code.Length && IsIdentifierChar(code[end]))
            end++;

        if (start == end)
            return null;

        // Compute columns relative to the line start
        var lineStart = code.LastIndexOf('\n', Math.Max(start - 1, 0)) + 1;
        var startCol = start - lineStart;
        var endCol = end - lineStart;

        return (line, startCol, line, endCol);
    }

    /// <summary>
    /// Returns true if the character is valid within a Python identifier (letter, digit, or underscore).
    /// </summary>
    internal static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Returns true if the character is valid within a dotted expression (identifier char or dot).
    /// </summary>
    internal static bool IsIdentifierOrDot(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '.';

    /// <summary>
    /// Builds a combined source string from all previously executed cells plus the current cell.
    /// Returns the combined source and the number of prefix lines (for adjusting positions back to cell-relative).
    /// </summary>
    internal static (string CombinedSource, int PrefixLineCount) BuildCombinedSource(
        IReadOnlyList<string> executedSources, string currentCode)
    {
        if (executedSources.Count == 0)
            return (currentCode, 0);

        var prefix = string.Join("\n", executedSources) + "\n";
        var prefixLineCount = prefix.Split('\n').Length - 1; // number of complete lines
        return (prefix + currentCode, prefixLineCount);
    }
}
