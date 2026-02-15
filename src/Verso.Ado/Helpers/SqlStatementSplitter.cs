using System.Text;

namespace Verso.Ado.Helpers;

/// <summary>
/// Splits SQL text on <c>;</c> boundaries while respecting quoted strings and comments.
/// Also handles <c>GO</c> batch separators when SQL Server provider is detected.
/// </summary>
internal static class SqlStatementSplitter
{
    internal static IReadOnlyList<string> Split(string sql, bool handleGoBatches = false)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return Array.Empty<string>();

        var statements = new List<string>();
        var current = new StringBuilder();
        int i = 0;

        while (i < sql.Length)
        {
            // Single-line comment
            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    current.Append(sql[i]);
                    i++;
                }
                continue;
            }

            // Multi-line comment
            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                current.Append(sql[i]);
                current.Append(sql[i + 1]);
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    current.Append(sql[i]);
                    i++;
                }
                if (i + 1 < sql.Length)
                {
                    current.Append(sql[i]);
                    current.Append(sql[i + 1]);
                    i += 2;
                }
                continue;
            }

            // Single-quoted string
            if (sql[i] == '\'')
            {
                current.Append(sql[i]);
                i++;
                while (i < sql.Length)
                {
                    current.Append(sql[i]);
                    if (sql[i] == '\'')
                    {
                        i++;
                        // Handle escaped quotes ('')
                        if (i < sql.Length && sql[i] == '\'')
                        {
                            current.Append(sql[i]);
                            i++;
                            continue;
                        }
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Double-quoted identifier
            if (sql[i] == '"')
            {
                current.Append(sql[i]);
                i++;
                while (i < sql.Length)
                {
                    current.Append(sql[i]);
                    if (sql[i] == '"')
                    {
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Statement separator: semicolon
            if (sql[i] == ';')
            {
                var stmt = current.ToString().Trim();
                if (!string.IsNullOrEmpty(stmt))
                    statements.Add(stmt);
                current.Clear();
                i++;
                continue;
            }

            // GO batch separator (case-insensitive, must be on its own line)
            if (handleGoBatches && IsGoBatchSeparator(sql, i))
            {
                var stmt = current.ToString().Trim();
                if (!string.IsNullOrEmpty(stmt))
                    statements.Add(stmt);
                current.Clear();
                i += 2; // skip "GO"
                // Skip to end of line
                while (i < sql.Length && sql[i] != '\n')
                    i++;
                continue;
            }

            current.Append(sql[i]);
            i++;
        }

        // Add remaining statement
        var remaining = current.ToString().Trim();
        if (!string.IsNullOrEmpty(remaining))
            statements.Add(remaining);

        return statements;
    }

    private static bool IsGoBatchSeparator(string sql, int pos)
    {
        // Must be at start of line or start of string
        if (pos > 0 && sql[pos - 1] != '\n' && sql[pos - 1] != '\r')
            return false;

        // Must have at least 2 chars
        if (pos + 1 >= sql.Length)
            return false;

        // Check for "GO" (case-insensitive)
        if ((sql[pos] != 'G' && sql[pos] != 'g') || (sql[pos + 1] != 'O' && sql[pos + 1] != 'o'))
            return false;

        // Must be followed by end of string, whitespace, or newline
        if (pos + 2 < sql.Length && !char.IsWhiteSpace(sql[pos + 2]))
            return false;

        return true;
    }
}
