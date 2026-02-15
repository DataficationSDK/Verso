namespace Verso.Ado.Helpers;

/// <summary>
/// Parses <c>--key value</c> and <c>--flag</c> syntax from magic command arguments.
/// Quoted string values (single and double quotes) are supported.
/// Flag arguments (no value) are stored with a <c>null</c> value.
/// </summary>
internal static class ArgumentParser
{
    internal static Dictionary<string, string?> Parse(string arguments)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(arguments))
            return result;

        var tokens = Tokenize(arguments);
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!token.StartsWith("--"))
                continue;

            var key = token.Substring(2);
            if (string.IsNullOrEmpty(key))
                continue;

            // Check if next token is a value (not another --key)
            if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith("--"))
            {
                result[key] = tokens[i + 1];
                i++; // skip the value token
            }
            else
            {
                result[key] = null; // flag
            }
        }

        return result;
    }

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        int i = 0;

        while (i < input.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(input[i]))
            {
                i++;
                continue;
            }

            // Quoted string
            if (input[i] == '"' || input[i] == '\'')
            {
                var quote = input[i];
                i++;
                int start = i;
                while (i < input.Length && input[i] != quote)
                    i++;
                tokens.Add(input.Substring(start, i - start));
                if (i < input.Length) i++; // skip closing quote
                continue;
            }

            // Unquoted token
            {
                int start = i;
                while (i < input.Length && !char.IsWhiteSpace(input[i]))
                    i++;
                tokens.Add(input.Substring(start, i - start));
            }
        }

        return tokens;
    }
}
