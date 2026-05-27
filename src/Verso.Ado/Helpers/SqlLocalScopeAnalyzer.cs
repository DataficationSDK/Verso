using System.Text;
using System.Text.RegularExpressions;

namespace Verso.Ado.Helpers;

/// <summary>
/// Extracts names that are introduced locally by the SQL itself (and therefore
/// must not be treated as references to the kernel variable store):
/// <list type="bullet">
///   <item>T-SQL <c>DECLARE @x</c> locals (single, multi-var, TABLE, CURSOR variants)</item>
///   <item>T-SQL <c>CREATE</c>/<c>ALTER PROCEDURE</c> and <c>CREATE</c>/<c>ALTER FUNCTION</c> parameter lists</item>
///   <item>MySQL session user variables introduced by <c>SET @x = …</c> or <c>SELECT @x := …</c></item>
/// </list>
/// </summary>
internal static class SqlLocalScopeAnalyzer
{
    private static readonly Regex DeclarePattern = new(
        @"\bDECLARE\b(?<list>[^;]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ProcedurePattern = new(
        @"\b(?:CREATE|ALTER)\s+PROC(?:EDURE)?\s+\S+(?<list>[\s\S]*?)\bAS\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FunctionPattern = new(
        @"\b(?:CREATE|ALTER)\s+FUNCTION\s+\S+(?<list>[\s\S]*?)\bRETURNS\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MySqlSetPattern = new(
        @"\bSET\s+@(\w+)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MySqlAssignPattern = new(
        @"@(\w+)\s*:=",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns the set of locally-introduced parameter names (without leading <c>@</c>)
    /// for the given SQL text and dialect. The set uses ordinal case-insensitive
    /// comparison, matching how the binder looks up names.
    /// </summary>
    internal static HashSet<string> FindLocalNames(string sql, SqlDialect dialect)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(sql))
            return names;

        // Strip comments and quoted contents so DECLARE-in-comment or
        // DECLARE-in-string-literal doesn't pollute the local set.
        var sanitized = StripNonCode(sql);

        switch (dialect)
        {
            case SqlDialect.SqlServer:
            case SqlDialect.Sqlite:
            case SqlDialect.Unknown:
                AddTSqlLocals(sanitized, names);
                break;

            case SqlDialect.MySql:
                AddMySqlLocals(sanitized, names);
                break;

            case SqlDialect.Postgres:
            case SqlDialect.Oracle:
            default:
                // No introduction rules for these dialects at the notebook-cell level.
                break;
        }

        return names;
    }

    private static void AddTSqlLocals(string sanitized, HashSet<string> names)
    {
        foreach (Match m in DeclarePattern.Matches(sanitized))
        {
            // DECLARE syntax is never parenthesized at the top level; each
            // comma-separated item is a single local declaration whose name
            // is the first @token. Subsequent @tokens are initializer references.
            ExtractFirstAtPerItem(m.Groups["list"].Value, names);
        }

        foreach (Match m in ProcedurePattern.Matches(sanitized))
        {
            ExtractParameterListNames(m.Groups["list"].Value, names);
        }

        foreach (Match m in FunctionPattern.Matches(sanitized))
        {
            ExtractParameterListNames(m.Groups["list"].Value, names);
        }
    }

    private static void AddMySqlLocals(string sanitized, HashSet<string> names)
    {
        foreach (Match m in MySqlSetPattern.Matches(sanitized))
            names.Add(m.Groups[1].Value);

        foreach (Match m in MySqlAssignPattern.Matches(sanitized))
            names.Add(m.Groups[1].Value);
    }

    /// <summary>
    /// Locates the parameter list in a stored-procedure or function header capture
    /// (which may or may not be enclosed in parens) and extracts each parameter
    /// name (the first <c>@token</c> in each top-level comma-separated item).
    /// </summary>
    private static void ExtractParameterListNames(string list, HashSet<string> names)
    {
        // Find the first non-whitespace character. If it's '(', the param list is
        // parenthesized — extract the balanced contents. Otherwise treat the entire
        // capture as the param list (T-SQL PROC supports both forms).
        int firstNonWs = 0;
        while (firstNonWs < list.Length && char.IsWhiteSpace(list[firstNonWs]))
            firstNonWs++;

        if (firstNonWs < list.Length && list[firstNonWs] == '(')
        {
            int depth = 0;
            int close = -1;
            for (int i = firstNonWs; i < list.Length; i++)
            {
                if (list[i] == '(') depth++;
                else if (list[i] == ')')
                {
                    depth--;
                    if (depth == 0) { close = i; break; }
                }
            }
            if (close > firstNonWs)
            {
                ExtractFirstAtPerItem(
                    list.Substring(firstNonWs + 1, close - firstNonWs - 1),
                    names);
                return;
            }
        }

        ExtractFirstAtPerItem(list, names);
    }

    /// <summary>
    /// Splits <paramref name="list"/> on commas at paren-depth 0 and, for each item,
    /// extracts the first <c>@name</c> token. Initializer references that appear
    /// after the name (e.g. <c>= @other</c> or <c>= @@SERVERNAME</c>) are ignored.
    /// </summary>
    private static void ExtractFirstAtPerItem(string list, HashSet<string> names)
    {
        int depth = 0;
        int itemStart = 0;

        for (int i = 0; i < list.Length; i++)
        {
            char c = list[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == ',' && depth == 0)
            {
                AddFirstAtName(list, itemStart, i, names);
                itemStart = i + 1;
            }
        }
        AddFirstAtName(list, itemStart, list.Length, names);
    }

    private static void AddFirstAtName(string list, int start, int end, HashSet<string> names)
    {
        int i = start;
        while (i < end && char.IsWhiteSpace(list[i])) i++;
        if (i >= end || list[i] != '@') return;
        // Skip the @@global form — it's not a local introduction.
        if (i + 1 < end && list[i + 1] == '@') return;

        int nameStart = i + 1;
        int nameEnd = nameStart;
        while (nameEnd < end && (char.IsLetterOrDigit(list[nameEnd]) || list[nameEnd] == '_'))
            nameEnd++;

        if (nameEnd > nameStart)
            names.Add(list.Substring(nameStart, nameEnd - nameStart));
    }

    /// <summary>
    /// Returns a copy of <paramref name="sql"/> with comments and the contents of
    /// string literals / quoted identifiers replaced by spaces. Character positions
    /// are preserved so regex offsets remain meaningful relative to the original.
    /// Delimiters themselves are preserved so word boundaries outside the literal
    /// stay intact.
    /// </summary>
    private static string StripNonCode(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        int i = 0;
        int len = sql.Length;

        while (i < len)
        {
            char c = sql[i];

            if (c == '-' && i + 1 < len && sql[i + 1] == '-')
            {
                while (i < len && sql[i] != '\n')
                {
                    sb.Append(' ');
                    i++;
                }
                continue;
            }

            if (c == '/' && i + 1 < len && sql[i + 1] == '*')
            {
                sb.Append("  ");
                i += 2;
                while (i < len)
                {
                    if (sql[i] == '*' && i + 1 < len && sql[i + 1] == '/')
                    {
                        sb.Append("  ");
                        i += 2;
                        break;
                    }
                    sb.Append(sql[i] == '\n' ? '\n' : ' ');
                    i++;
                }
                continue;
            }

            if ((c == 'N' || c == 'n')
                && i + 1 < len && sql[i + 1] == '\''
                && (i == 0 || !IsWordChar(sql[i - 1])))
            {
                sb.Append(c);
                sb.Append('\'');
                i += 2;
                EraseSingleQuotedTo(sql, ref i, sb);
                continue;
            }

            if (c == '\'')
            {
                sb.Append('\'');
                i++;
                EraseSingleQuotedTo(sql, ref i, sb);
                continue;
            }

            if (c == '"')
            {
                sb.Append('"');
                i++;
                while (i < len && sql[i] != '"')
                {
                    sb.Append(sql[i] == '\n' ? '\n' : ' ');
                    i++;
                }
                if (i < len)
                {
                    sb.Append('"');
                    i++;
                }
                continue;
            }

            if (c == '[')
            {
                sb.Append('[');
                i++;
                while (i < len && sql[i] != ']')
                {
                    sb.Append(sql[i] == '\n' ? '\n' : ' ');
                    i++;
                }
                if (i < len)
                {
                    sb.Append(']');
                    i++;
                }
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static void EraseSingleQuotedTo(string sql, ref int i, StringBuilder sb)
    {
        int len = sql.Length;
        while (i < len)
        {
            if (sql[i] == '\'')
            {
                if (i + 1 < len && sql[i + 1] == '\'')
                {
                    sb.Append("  ");
                    i += 2;
                    continue;
                }
                sb.Append('\'');
                i++;
                return;
            }
            sb.Append(sql[i] == '\n' ? '\n' : ' ');
            i++;
        }
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';
}
