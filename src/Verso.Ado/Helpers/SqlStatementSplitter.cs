using System.Text;

namespace Verso.Ado.Helpers;

/// <summary>
/// Splits SQL text on <c>;</c> boundaries while respecting quoted strings, comments,
/// and <c>BEGIN ... END</c> control-flow blocks. Also handles <c>GO</c> batch
/// separators when SQL Server provider is detected.
/// </summary>
/// <remarks>
/// Semicolon splitting can be disabled via <paramref name="splitOnSemicolon"/>. On
/// SQL Server a semicolon is only a statement terminator, not a batch boundary, so the
/// whole cell (or each <c>GO</c>-delimited batch) must be sent to the server as a single
/// command. Splitting on <c>;</c> there would run each statement as its own batch and
/// drop the shared variable scope, breaking scripts such as <c>DECLARE @x …; EXEC … @x …</c>.
/// </remarks>
internal static class SqlStatementSplitter
{
    internal static IReadOnlyList<string> Split(
        string sql, bool handleGoBatches = false, bool splitOnSemicolon = true)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return Array.Empty<string>();

        var statements = new List<string>();
        var current = new StringBuilder();
        int blockDepth = 0;
        int caseDepth = 0;
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

            // Block-tracking keywords (BEGIN / END / CASE) — must be whole words.
            if (IsWordBoundaryStart(sql, i))
            {
                if (MatchKeyword(sql, i, "BEGIN"))
                {
                    // BEGIN TRANSACTION / TRAN / DISTRIBUTED / DIALOG / CONVERSATION
                    // do not pair with END, so they must not increment block depth.
                    if (!IsTransactionalBegin(sql, i + 5))
                        blockDepth++;
                    current.Append(sql, i, 5);
                    i += 5;
                    continue;
                }

                if (MatchKeyword(sql, i, "CASE"))
                {
                    caseDepth++;
                    current.Append(sql, i, 4);
                    i += 4;
                    continue;
                }

                if (MatchKeyword(sql, i, "END"))
                {
                    // END pairs with the nearest enclosing CASE first, then BEGIN.
                    // `END TRY` / `END CATCH` are just a regular END followed by a
                    // separate keyword and are handled by the natural decrement.
                    if (caseDepth > 0)
                        caseDepth--;
                    else if (blockDepth > 0)
                        blockDepth--;
                    current.Append(sql, i, 3);
                    i += 3;
                    continue;
                }
            }

            // Statement separator: semicolon
            if (splitOnSemicolon && sql[i] == ';')
            {
                if (blockDepth > 0)
                {
                    // Inside a BEGIN...END block — semicolon is statement-internal.
                    current.Append(sql[i]);
                    i++;
                    continue;
                }

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

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    private static bool IsWordBoundaryStart(string sql, int pos) =>
        pos == 0 || !IsWordChar(sql[pos - 1]);

    private static bool MatchKeyword(string sql, int pos, string keyword)
    {
        if (pos + keyword.Length > sql.Length)
            return false;

        for (int k = 0; k < keyword.Length; k++)
        {
            if (char.ToUpperInvariant(sql[pos + k]) != keyword[k])
                return false;
        }

        int after = pos + keyword.Length;
        return after == sql.Length || !IsWordChar(sql[after]);
    }

    private static bool IsTransactionalBegin(string sql, int afterBegin)
    {
        // Skip whitespace after BEGIN
        int p = afterBegin;
        while (p < sql.Length && char.IsWhiteSpace(sql[p]))
            p++;

        if (p >= sql.Length)
            return false;

        return MatchKeyword(sql, p, "TRANSACTION")
            || MatchKeyword(sql, p, "TRAN")
            || MatchKeyword(sql, p, "DISTRIBUTED")
            || MatchKeyword(sql, p, "DIALOG")
            || MatchKeyword(sql, p, "CONVERSATION");
    }
}
