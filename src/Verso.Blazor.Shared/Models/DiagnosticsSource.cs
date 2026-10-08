namespace Verso.Blazor.Shared.Models;

/// <summary>
/// Prepares a cell's source for a diagnostics request.
/// </summary>
/// <remarks>
/// The execution pipeline strips leading <c>#!</c> magic command lines before the kernel sees a
/// cell, but a diagnostics request carries the source as typed. Left in, a line such as
/// <c>#!time</c> reads as a syntax error to every kernel. Each magic line is blanked rather than
/// removed, so the line numbers the kernel reports still point at the right lines in the editor.
/// </remarks>
internal static class DiagnosticsSource
{
    /// <summary>
    /// Returns <paramref name="source"/> with its leading magic command lines emptied. Matches
    /// what the pipeline treats as leading: magic lines and blank lines up to the first line
    /// that is neither.
    /// </summary>
    public static string MaskLeadingMagicCommands(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;

        System.Text.StringBuilder? masked = null;
        var copiedTo = 0;
        var lineStart = 0;

        while (lineStart < source.Length)
        {
            var lineEnd = lineStart;
            while (lineEnd < source.Length && source[lineEnd] is not '\r' and not '\n')
                lineEnd++;

            var pos = lineStart;
            while (pos < lineEnd && source[pos] is ' ' or '\t')
                pos++;

            if (pos < lineEnd)
            {
                if (!IsMagicCommandLine(source, pos, lineEnd))
                    break;

                // Keep everything up to this line, drop its text, and resume at its line break.
                masked ??= new System.Text.StringBuilder(source.Length);
                masked.Append(source, copiedTo, lineStart - copiedTo);
                copiedTo = lineEnd;
            }

            lineStart = lineEnd;
            if (lineStart < source.Length && source[lineStart] == '\r') lineStart++;
            if (lineStart < source.Length && source[lineStart] == '\n') lineStart++;
        }

        if (masked is null) return source;
        masked.Append(source, copiedTo, source.Length - copiedTo);
        return masked.ToString();
    }

    // A magic line is "#!" followed by a command name; "#!" on its own is not one.
    private static bool IsMagicCommandLine(string source, int pos, int lineEnd)
    {
        if (pos + 1 >= lineEnd || source[pos] != '#' || source[pos + 1] != '!')
            return false;

        for (var i = pos + 2; i < lineEnd; i++)
        {
            if (!char.IsWhiteSpace(source[i]))
                return true;
        }

        return false;
    }
}
