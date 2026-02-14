namespace Verso.MagicCommands;

/// <summary>
/// Parses cell source to detect <c>#!</c>-prefixed magic commands on the first non-empty line.
/// </summary>
internal static class MagicCommandParser
{
    public record ParseResult(bool IsMagicCommand, string? CommandName, string? Arguments, string RemainingCode);

    /// <summary>
    /// Parses the given source code. If the first non-empty line starts with <c>#!</c>,
    /// extracts the command name, arguments, and remaining code.
    /// </summary>
    public static ParseResult Parse(string? source)
    {
        if (string.IsNullOrEmpty(source))
            return new ParseResult(false, null, null, source ?? "");

        // Find the first non-empty line
        var span = source.AsSpan();
        int lineStart = 0;

        // Skip leading whitespace lines
        while (lineStart < span.Length)
        {
            // Skip whitespace on this line (spaces/tabs only, not newlines)
            int pos = lineStart;
            while (pos < span.Length && span[pos] is ' ' or '\t')
                pos++;

            // If we hit a newline or end, this is a blank line — skip it
            if (pos >= span.Length)
            {
                lineStart = pos;
                break;
            }

            if (span[pos] is '\r' or '\n')
            {
                lineStart = span[pos] == '\r' && pos + 1 < span.Length && span[pos + 1] == '\n'
                    ? pos + 2
                    : pos + 1;
                continue;
            }

            // We found the first non-empty content at 'pos'
            if (pos + 1 < span.Length && span[pos] == '#' && span[pos + 1] == '!')
            {
                // This is a magic command line
                var commandStart = pos + 2; // after "#!"

                // Find end of this line
                var lineEnd = commandStart;
                while (lineEnd < span.Length && span[lineEnd] is not '\r' and not '\n')
                    lineEnd++;

                var commandLine = span.Slice(commandStart, lineEnd - commandStart).ToString().Trim();

                // Extract command name (first token) and arguments (rest)
                string commandName;
                string arguments;

                var spaceIndex = commandLine.IndexOf(' ');
                if (spaceIndex < 0)
                {
                    commandName = commandLine;
                    arguments = "";
                }
                else
                {
                    commandName = commandLine.Substring(0, spaceIndex);
                    arguments = commandLine.Substring(spaceIndex + 1).Trim();
                }

                if (string.IsNullOrEmpty(commandName))
                    return new ParseResult(false, null, null, source);

                // Remaining code is everything after the first line
                var remainingStart = lineEnd;
                if (remainingStart < span.Length && span[remainingStart] == '\r')
                    remainingStart++;
                if (remainingStart < span.Length && span[remainingStart] == '\n')
                    remainingStart++;

                var remaining = remainingStart < span.Length
                    ? span.Slice(remainingStart).ToString()
                    : "";

                return new ParseResult(true, commandName, arguments, remaining);
            }

            // First non-empty line does not start with #!
            return new ParseResult(false, null, null, source);
        }

        // All lines were empty
        return new ParseResult(false, null, null, source);
    }
}
