namespace Verso.Ado.Helpers;

/// <summary>
/// Single-pass character walker that finds <c>@name</c> parameter references
/// in SQL text while correctly skipping comments, string literals, and quoted
/// identifiers. Recognized contexts:
/// <list type="bullet">
///   <item><c>-- single-line</c> comments through end of line</item>
///   <item><c>/* block */</c> comments</item>
///   <item><c>'…'</c> and <c>N'…'</c> string literals (including doubled-quote escapes)</item>
///   <item><c>"…"</c> double-quoted identifiers (ANSI / Postgres / Sqlite)</item>
///   <item><c>[…]</c> bracketed identifiers (T-SQL)</item>
/// </list>
/// The leading <c>@</c> of a T-SQL global like <c>@@SPID</c> is not emitted as a
/// reference (the lookbehind on the previous character mirrors the prior regex).
/// Oracle uses <c>:name</c> binds, so this scanner emits no references for
/// <see cref="SqlDialect.Oracle"/>.
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
        if (string.IsNullOrEmpty(sql) || dialect == SqlDialect.Oracle)
            return Array.Empty<ParameterReference>();

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

                    // Parameter candidate: @name
                    if (c == '@')
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

                        int nameStart = i + 1;
                        int nameEnd = nameStart;
                        while (nameEnd < len && IsNameChar(sql[nameEnd]))
                            nameEnd++;

                        if (nameEnd > nameStart)
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

    private static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';
}
