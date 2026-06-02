namespace Verso.Ado.Helpers;

/// <summary>
/// Single-pass character walker that finds named parameter references in SQL
/// text while correctly skipping comments, string literals, and quoted
/// identifiers. The prefix character is dialect-dependent: <c>@name</c> for
/// SQL Server, PostgreSQL, MySQL, and SQLite; <c>:name</c> for Oracle (see
/// <see cref="SqlDialectExtensions.ParameterPrefix"/>). Recognized contexts:
/// <list type="bullet">
///   <item><c>-- single-line</c> comments through end of line</item>
///   <item><c>/* block */</c> comments</item>
///   <item><c>'…'</c> and <c>N'…'</c> string literals (including doubled-quote escapes)</item>
///   <item><c>"…"</c> double-quoted identifiers (ANSI / Postgres / Sqlite / Oracle)</item>
///   <item><c>[…]</c> bracketed identifiers (T-SQL)</item>
/// </list>
/// The leading <c>@</c> of a T-SQL global like <c>@@SPID</c> is not emitted as a
/// reference (the lookbehind on the previous character mirrors the prior regex).
/// For Oracle, a colon only begins a bind when it is followed by an identifier
/// character, so the PL/SQL assignment operator <c>:=</c>, positional binds such
/// as <c>:1</c>, and stray <c>::</c> are not treated as references; the trigger
/// pseudorecords <c>:new</c> and <c>:old</c> are likewise excluded.
/// </summary>
internal static class SqlParameterScanner
{
    private enum CharContext
    {
        Code,
        SingleLineComment,
        BlockComment,
        SingleQuotedString,
        DoubleQuotedIdentifier,
        BracketedIdentifier,
    }

    /// <summary>
    /// Scans <paramref name="sql"/> and returns parameter references found in code context.
    /// </summary>
    internal static IReadOnlyList<ParameterReference> Scan(string sql, SqlDialect dialect)
    {
        if (string.IsNullOrEmpty(sql))
            return Array.Empty<ParameterReference>();

        char prefix = dialect.ParameterPrefix();

        var results = new List<ParameterReference>();
        var context = CharContext.Code;
        int i = 0;
        int len = sql.Length;

        while (i < len)
        {
            char c = sql[i];

            switch (context)
            {
                case CharContext.Code:
                    // Comment: --
                    if (c == '-' && i + 1 < len && sql[i + 1] == '-')
                    {
                        context = CharContext.SingleLineComment;
                        i += 2;
                        continue;
                    }

                    // Comment: /*
                    if (c == '/' && i + 1 < len && sql[i + 1] == '*')
                    {
                        context = CharContext.BlockComment;
                        i += 2;
                        continue;
                    }

                    // Unicode literal: N'...'
                    if ((c == 'N' || c == 'n')
                        && i + 1 < len && sql[i + 1] == '\''
                        && (i == 0 || !IsWordChar(sql[i - 1])))
                    {
                        context = CharContext.SingleQuotedString;
                        i += 2;
                        continue;
                    }

                    // Single-quoted string
                    if (c == '\'')
                    {
                        context = CharContext.SingleQuotedString;
                        i++;
                        continue;
                    }

                    // Double-quoted identifier
                    if (c == '"')
                    {
                        context = CharContext.DoubleQuotedIdentifier;
                        i++;
                        continue;
                    }

                    // Bracketed identifier
                    if (c == '[')
                    {
                        context = CharContext.BracketedIdentifier;
                        i++;
                        continue;
                    }

                    // Parameter candidate: <prefix>name
                    if (c == prefix)
                    {
                        if (prefix == '@')
                        {
                            // Skip the second @ in @@globals (e.g. @@SPID)
                            bool precededByAt = i > 0 && sql[i - 1] == '@';
                            // Skip the @ itself if the next char is also @ (we'll see it as
                            // the second @ on the next iteration and won't emit).
                            bool followedByAt = i + 1 < len && sql[i + 1] == '@';

                            if (precededByAt || followedByAt)
                            {
                                i++;
                                continue;
                            }
                        }
                        else
                        {
                            // Oracle ':' only begins a bind when followed by an
                            // identifier character. This skips the PL/SQL assignment
                            // operator ':=', positional binds like ':1', and stray '::'.
                            char next = i + 1 < len ? sql[i + 1] : '\0';
                            if (!(char.IsLetter(next) || next == '_'))
                            {
                                i++;
                                continue;
                            }
                        }

                        int nameStart = i + 1;
                        int nameEnd = nameStart;
                        while (nameEnd < len && IsNameChar(sql[nameEnd]))
                            nameEnd++;

                        if (nameEnd > nameStart && !IsExcludedName(prefix, sql, nameStart, nameEnd))
                        {
                            results.Add(new ParameterReference(
                                Name: sql.Substring(nameStart, nameEnd - nameStart),
                                Offset: i,
                                Length: nameEnd - i));
                        }

                        i = nameEnd > nameStart ? nameEnd : i + 1;
                        continue;
                    }

                    i++;
                    break;

                case CharContext.SingleLineComment:
                    if (c == '\n')
                        context = CharContext.Code;
                    i++;
                    break;

                case CharContext.BlockComment:
                    if (c == '*' && i + 1 < len && sql[i + 1] == '/')
                    {
                        context = CharContext.Code;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                    break;

                case CharContext.SingleQuotedString:
                    if (c == '\'')
                    {
                        // Doubled single quote = escaped quote, stay in string.
                        if (i + 1 < len && sql[i + 1] == '\'')
                        {
                            i += 2;
                        }
                        else
                        {
                            context = CharContext.Code;
                            i++;
                        }
                    }
                    else
                    {
                        i++;
                    }
                    break;

                case CharContext.DoubleQuotedIdentifier:
                    if (c == '"')
                    {
                        context = CharContext.Code;
                    }
                    i++;
                    break;

                case CharContext.BracketedIdentifier:
                    if (c == ']')
                    {
                        context = CharContext.Code;
                    }
                    i++;
                    break;
            }
        }

        return results;
    }

    /// <summary>
    /// Names that look like parameters syntactically but are not binds. For
    /// Oracle this excludes the trigger pseudorecords <c>:new</c> and <c>:old</c>.
    /// </summary>
    private static bool IsExcludedName(char prefix, string sql, int nameStart, int nameEnd)
    {
        if (prefix != ':')
            return false;

        int length = nameEnd - nameStart;
        if (length != 3)
            return false;

        ReadOnlySpan<char> name = sql.AsSpan(nameStart, length);
        return name.Equals("new", StringComparison.OrdinalIgnoreCase)
            || name.Equals("old", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';
}
