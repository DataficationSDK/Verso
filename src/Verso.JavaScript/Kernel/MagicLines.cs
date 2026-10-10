using System.Text;

namespace Verso.JavaScript.Kernel;

/// <summary>
/// Helpers for the <c>#!</c> magic command lines a cell may open with.
/// </summary>
internal static class MagicLines
{
    /// <summary>
    /// Empty the leading run of blank and <c>#!</c> magic command lines, keeping every line break so that
    /// line and column positions in the rest of the cell are unchanged. The execution pipeline
    /// strips those lines before a cell runs, but the editor asks for diagnostics on the raw
    /// source, where a parser would otherwise read <c>#!npm lodash</c> as a syntax error.
    /// </summary>
    public static string BlankLeadingDirectives(string code)
    {
        if (string.IsNullOrEmpty(code))
            return code;

        var builder = new StringBuilder(code.Length);
        var pos = 0;
        var blanked = false;

        while (pos < code.Length)
        {
            var lineEnd = pos;
            while (lineEnd < code.Length && code[lineEnd] is not '\r' and not '\n')
                lineEnd++;

            var content = code.AsSpan(pos, lineEnd - pos).TrimStart();
            if (!content.IsEmpty && !IsMagicCommand(content))
                break;

            if (!content.IsEmpty)
                blanked = true;

            // Keep the line's break exactly as written, \r\n, \n or a lone \r.
            var next = lineEnd;
            if (next < code.Length && code[next] == '\r')
                next++;
            if (next < code.Length && code[next] == '\n')
                next++;

            builder.Append(code, lineEnd, next - lineEnd);
            pos = next;
        }

        if (!blanked)
            return code;

        builder.Append(code, pos, code.Length - pos);
        return builder.ToString();
    }

    // The pipeline's rule: "#!" followed by a command name. A bare "#!" names no command, so the
    // pipeline leaves it, and the lines after it, for the kernel.
    private static bool IsMagicCommand(ReadOnlySpan<char> line)
        => line.StartsWith("#!", StringComparison.Ordinal) && !line[2..].IsWhiteSpace();
}
