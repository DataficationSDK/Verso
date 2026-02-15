using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Ado.Formatters;
using Verso.Ado.Helpers;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;

namespace Verso.Ado.Kernel;

/// <summary>
/// Language kernel for executing SQL against ADO.NET database connections.
/// Results are published as <see cref="DataTable"/> to the variable store.
/// Accessed through <see cref="CellType.SqlCellType"/>; not independently registered.
/// </summary>
public sealed class SqlKernel : ILanguageKernel
{
    private static readonly Regex ParamPattern = new(@"@(\w+)", RegexOptions.Compiled);
    private readonly SemaphoreSlim _executionLock = new(1, 1);

    internal const int DefaultMaxFetchRows = 10_000;
    private const int DefaultDisplayPageSize = 50;

    private IVariableStore? _lastVariableStore;
    private readonly SchemaCache _schemaCache = SchemaCache.Instance;

    // --- IExtension ---
    public string ExtensionId => "verso.ado.kernel.sql";
    string IExtension.Name => "SQL Kernel";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Executes SQL queries against ADO.NET database connections.";

    // --- ILanguageKernel ---
    public string LanguageId => "sql";
    public string DisplayName => "SQL";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".sql" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        // Capture variable store for language service methods
        _lastVariableStore = context.Variables;

        var outputs = new List<CellOutput>();

        // Parse directives
        var (directives, sqlCode) = SqlDirectives.Parse(code);

        if (string.IsNullOrWhiteSpace(sqlCode))
        {
            outputs.Add(new CellOutput("text/plain", "No SQL to execute.", IsError: true));
            return outputs;
        }

        // Resolve connection
        var connInfo = ConnectionResolver.Resolve(directives.ConnectionName, context.Variables);
        if (connInfo is null)
        {
            outputs.Add(new CellOutput("text/plain",
                "No database connection. Use `#!sql-connect` to establish a connection.", IsError: true));
            return outputs;
        }

        if (connInfo.Connection is null || connInfo.Connection.State != ConnectionState.Open)
        {
            outputs.Add(new CellOutput("text/plain",
                $"Connection '{connInfo.Name}' is not open. Reconnect with `#!sql-connect`.", IsError: true));
            return outputs;
        }

        // Determine if this is a SQL Server provider (for GO batch handling)
        bool isSqlServer = connInfo.ProviderName?.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) ?? false;

        // Split statements
        var statements = SqlStatementSplitter.Split(sqlCode, handleGoBatches: isSqlServer);

        int maxRows = directives.PageSize ?? DefaultMaxFetchRows;
        int displayPageSize = DefaultDisplayPageSize;

        await _executionLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            SqlResultSet? lastResultSet = null;

            // Accumulate consecutive non-query results into a single summary
            int pendingAffected = 0;
            int pendingStatements = 0;
            long pendingElapsedMs = 0;

            foreach (var statement in statements)
            {
                var sw = Stopwatch.StartNew();

                using var cmd = connInfo.Connection.CreateCommand();
                cmd.CommandText = statement;

                // Bind parameters
                BindParameters(cmd, statement, context.Variables, outputs);

                using var reader = await cmd.ExecuteReaderAsync(context.CancellationToken).ConfigureAwait(false);

                sw.Stop();

                if (reader.HasRows || reader.FieldCount > 0)
                {
                    // Flush any pending non-query summary before showing query results
                    FlushNonQuerySummary(outputs, ref pendingAffected, ref pendingStatements, ref pendingElapsedMs, directives, context);

                    var resultSet = await ReadResultSetAsync(reader, maxRows, context.CancellationToken)
                        .ConfigureAwait(false);
                    lastResultSet = resultSet;

                    if (!directives.NoDisplay)
                    {
                        var html = ResultSetFormatter.FormatResultSetHtml(resultSet, context.Theme, displayPageSize);
                        outputs.Add(new CellOutput("text/html", html));
                    }
                }
                else
                {
                    var affected = reader.RecordsAffected;
                    if (affected >= 0)
                    {
                        pendingAffected += affected;
                        pendingStatements++;
                        pendingElapsedMs += sw.ElapsedMilliseconds;
                    }
                }
            }

            // Flush any remaining non-query summary
            FlushNonQuerySummary(outputs, ref pendingAffected, ref pendingStatements, ref pendingElapsedMs, directives, context);

            // Publish last result to variable store as DataTable and SqlResultSet
            if (lastResultSet is not null)
            {
                var variableName = directives.VariableName ?? "lastSqlResult";
                var dataTable = ToDataTable(lastResultSet);
                context.Variables.Set(variableName, dataTable);
                context.Variables.Set($"{variableName}__resultset", lastResultSet);

                // Store cell-to-variable mapping for export actions
                context.Variables.Set($"__verso_ado_cellvar_{context.CellId}", variableName);
            }
        }
        catch (Exception ex)
        {
            outputs.Add(new CellOutput("text/plain", $"SQL error: {ex.Message}", IsError: true));
        }
        finally
        {
            _executionLock.Release();
        }

        return outputs;
    }

    private static void FlushNonQuerySummary(
        List<CellOutput> outputs,
        ref int pendingAffected,
        ref int pendingStatements,
        ref long pendingElapsedMs,
        SqlDirectives directives,
        IExecutionContext context)
    {
        if (pendingStatements == 0 || directives.NoDisplay)
        {
            pendingAffected = 0;
            pendingStatements = 0;
            pendingElapsedMs = 0;
            return;
        }

        var html = ResultSetFormatter.FormatNonQueryHtml(pendingAffected, pendingStatements, pendingElapsedMs, context.Theme);
        outputs.Add(new CellOutput("text/html", html));

        pendingAffected = 0;
        pendingStatements = 0;
        pendingElapsedMs = 0;
    }

    // --- Completions ---

    public async Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        var completions = new List<Completion>();
        var partial = ExtractPartialWord(code, cursorPosition);
        var context = DetermineSqlContext(code, cursorPosition);

        // Schema-based completions (tables, columns)
        SchemaCacheEntry? schemaEntry = null;
        if (_lastVariableStore is not null)
        {
            var connInfo = ResolveDefaultConnection(_lastVariableStore);
            if (connInfo?.Connection is not null && connInfo.Connection.State == ConnectionState.Open)
            {
                try
                {
                    schemaEntry = await _schemaCache.GetOrRefreshAsync(
                        connInfo.Name, connInfo.Connection).ConfigureAwait(false);
                }
                catch
                {
                    // Graceful degradation — keywords only
                }
            }
        }

        // Table completions
        if (schemaEntry is not null)
        {
            foreach (var table in schemaEntry.Tables)
            {
                if (MatchesPrefix(table.Name, partial))
                {
                    completions.Add(new Completion(
                        table.Name,
                        table.Name,
                        "Class",
                        $"{table.TableType}: {(table.Schema is not null ? $"{table.Schema}.{table.Name}" : table.Name)}",
                        $"0_{table.Name}"));
                }
            }

            // Column completions
            foreach (var (tableName, columns) in schemaEntry.Columns)
            {
                foreach (var col in columns)
                {
                    if (MatchesPrefix(col.Name, partial))
                    {
                        completions.Add(new Completion(
                            col.Name,
                            col.Name,
                            "Property",
                            $"{col.DataType} ({tableName}){(col.IsPrimaryKey ? " [PK]" : "")}{(col.IsNullable ? " NULL" : " NOT NULL")}",
                            $"0_{col.Name}"));
                    }
                }
            }
        }

        // @variable completions
        if (_lastVariableStore is not null)
        {
            foreach (var v in _lastVariableStore.GetAll())
            {
                if (v.Name.StartsWith("__verso_", StringComparison.Ordinal))
                    continue;

                var varName = $"@{v.Name}";
                if (MatchesPrefix(varName, partial) || MatchesPrefix(v.Name, partial))
                {
                    completions.Add(new Completion(
                        varName,
                        varName,
                        "Variable",
                        $"{v.Type.Name}: {TruncateValue(v.Value)}",
                        $"2_{v.Name}"));
                }
            }
        }

        // SQL keyword completions
        foreach (var kw in SqlKeywords)
        {
            if (MatchesPrefix(kw, partial))
            {
                completions.Add(new Completion(
                    kw, kw, "Keyword", null, $"1_{kw}"));
            }
        }

        return completions;
    }

    // --- Diagnostics ---

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        var diagnostics = new List<Diagnostic>();

        var (directives, sqlCode) = SqlDirectives.Parse(code);

        // Check for missing connection
        if (_lastVariableStore is not null)
        {
            var connInfo = ConnectionResolver.Resolve(directives.ConnectionName, _lastVariableStore);
            if (connInfo is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    directives.ConnectionName is not null
                        ? $"Connection '{directives.ConnectionName}' not found. Use #!sql-connect to establish a connection."
                        : "No database connection. Use #!sql-connect to establish a connection.",
                    0, 0, 0, 0));
            }
        }
        else
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "No database connection. Use #!sql-connect to establish a connection.",
                0, 0, 0, 0));
        }

        // Check for unresolved @parameters
        var matches = ParamPattern.Matches(code);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            var paramName = match.Groups[1].Value;
            if (!seen.Add(paramName))
                continue;

            bool resolved = false;
            if (_lastVariableStore is not null)
            {
                var allVars = _lastVariableStore.GetAll();
                resolved = allVars.Any(v =>
                    string.Equals(v.Name, paramName, StringComparison.OrdinalIgnoreCase));
            }

            if (!resolved)
            {
                var (startLine, startCol) = OffsetToLineCol(code, match.Index);
                var (endLine, endCol) = OffsetToLineCol(code, match.Index + match.Length);

                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Unresolved parameter '@{paramName}'. No matching variable found in the variable store.",
                    startLine, startCol, endLine, endCol));
            }
        }

        return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);
    }

    // --- Hover ---

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        var (word, wordStart, wordEnd) = ExtractWordAtCursor(code, cursorPosition);
        if (string.IsNullOrEmpty(word))
            return Task.FromResult<HoverInfo?>(null);

        var (startLine, startCol) = OffsetToLineCol(code, wordStart);
        var (endLine, endCol) = OffsetToLineCol(code, wordEnd);
        var range = (startLine, startCol, endLine, endCol);

        // Check @variable
        if (word.StartsWith('@') && _lastVariableStore is not null)
        {
            var varName = word.Substring(1);
            var allVars = _lastVariableStore.GetAll();
            var descriptor = allVars.FirstOrDefault(v =>
                string.Equals(v.Name, varName, StringComparison.OrdinalIgnoreCase));

            if (descriptor is not null)
            {
                var content = $"Variable @{descriptor.Name}\nType: {descriptor.Type.Name}\nValue: {TruncateValue(descriptor.Value)}";
                return Task.FromResult<HoverInfo?>(new HoverInfo(content, "text/plain", range));
            }
        }

        var lookup = word.StartsWith('@') ? word.Substring(1) : word;

        // Check keyword
        if (KeywordDescriptions.TryGetValue(lookup.ToUpperInvariant(), out var kwDescription))
        {
            return Task.FromResult<HoverInfo?>(new HoverInfo(kwDescription, "text/plain", range));
        }

        // Check schema cache for table/column
        if (_lastVariableStore is not null)
        {
            var connInfo = ResolveDefaultConnection(_lastVariableStore);
            if (connInfo is not null)
            {
                if (_schemaCache.TryGetCached(connInfo.Name, out var entry) && entry is not null)
                {
                    // Check table name
                    var table = entry.Tables.FirstOrDefault(t =>
                        string.Equals(t.Name, lookup, StringComparison.OrdinalIgnoreCase));
                    if (table is not null)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"{table.TableType}: {(table.Schema is not null ? $"{table.Schema}.{table.Name}" : table.Name)}");
                        if (entry.Columns.TryGetValue(table.Name, out var cols))
                        {
                            sb.AppendLine("Columns:");
                            foreach (var col in cols)
                            {
                                sb.AppendLine($"  {col.Name} {col.DataType}{(col.IsPrimaryKey ? " [PK]" : "")}{(col.IsNullable ? " NULL" : " NOT NULL")}");
                            }
                        }
                        return Task.FromResult<HoverInfo?>(new HoverInfo(sb.ToString().TrimEnd(), "text/plain", range));
                    }

                    // Check column name across all tables
                    foreach (var (tableName, columns) in entry.Columns)
                    {
                        var col = columns.FirstOrDefault(c =>
                            string.Equals(c.Name, lookup, StringComparison.OrdinalIgnoreCase));
                        if (col is not null)
                        {
                            var content = $"Column: {col.Name}\nType: {col.DataType}\nTable: {tableName}\nNullable: {(col.IsNullable ? "YES" : "NO")}{(col.IsPrimaryKey ? "\nPrimary Key" : "")}";
                            return Task.FromResult<HoverInfo?>(new HoverInfo(content, "text/plain", range));
                        }
                    }
                }
            }
        }

        return Task.FromResult<HoverInfo?>(null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // --- Private helpers ---

    private static SqlConnectionInfo? ResolveDefaultConnection(IVariableStore variables)
    {
        return ConnectionResolver.Resolve(null, variables);
    }

    private static string ExtractPartialWord(string code, int cursorPosition)
    {
        if (cursorPosition <= 0 || cursorPosition > code.Length)
            return "";

        int start = cursorPosition - 1;
        while (start >= 0 && IsWordChar(code[start]))
            start--;

        start++;
        return code.Substring(start, cursorPosition - start);
    }

    private static (string Word, int Start, int End) ExtractWordAtCursor(string code, int cursorPosition)
    {
        if (cursorPosition < 0 || cursorPosition > code.Length || code.Length == 0)
            return ("", 0, 0);

        // Adjust if cursor is at end or past a non-word character
        int pos = cursorPosition < code.Length ? cursorPosition : cursorPosition - 1;
        if (pos < 0 || (!IsWordChar(code[pos]) && code[pos] != '@'))
        {
            // Try one position back
            pos = cursorPosition - 1;
            if (pos < 0 || (!IsWordChar(code[pos]) && code[pos] != '@'))
                return ("", 0, 0);
        }

        int start = pos;
        int end = pos;

        // Scan backwards
        while (start > 0 && (IsWordChar(code[start - 1]) || code[start - 1] == '@'))
            start--;

        // Scan forwards
        while (end < code.Length - 1 && IsWordChar(code[end + 1]))
            end++;

        return (code.Substring(start, end - start + 1), start, end + 1);
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '.';

    private static string DetermineSqlContext(string code, int cursorPosition)
    {
        // Find the nearest preceding keyword to determine context
        var beforeCursor = code.Substring(0, Math.Min(cursorPosition, code.Length));
        var upper = beforeCursor.ToUpperInvariant();

        // Scan backwards for keywords
        string[] tableContextKeywords = { "FROM", "JOIN", "INTO", "UPDATE", "TABLE" };
        string[] columnContextKeywords = { "SELECT", "WHERE", "ON", "ORDER BY", "GROUP BY", "SET", "HAVING" };

        int lastTableKw = -1;
        int lastColKw = -1;

        foreach (var kw in tableContextKeywords)
        {
            int idx = upper.LastIndexOf(kw, StringComparison.Ordinal);
            if (idx > lastTableKw) lastTableKw = idx;
        }

        foreach (var kw in columnContextKeywords)
        {
            int idx = upper.LastIndexOf(kw, StringComparison.Ordinal);
            if (idx > lastColKw) lastColKw = idx;
        }

        if (lastTableKw > lastColKw) return "table";
        if (lastColKw > lastTableKw) return "column";
        return "general";
    }

    private static bool MatchesPrefix(string candidate, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return true;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static (int Line, int Column) OffsetToLineCol(string text, int offset)
    {
        int line = 0;
        int col = 0;
        for (int i = 0; i < offset && i < text.Length; i++)
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

    private static string TruncateValue(object? value, int maxLength = 100)
    {
        if (value is null) return "null";
        var str = value.ToString() ?? "null";
        return str.Length > maxLength ? str.Substring(0, maxLength) + "..." : str;
    }

    private static bool IsInsideStringLiteral(string sql, int position)
    {
        bool inString = false;
        for (int i = 0; i < position && i < sql.Length; i++)
        {
            if (sql[i] == '\'')
            {
                // Handle escaped single quotes ('')
                if (inString && i + 1 < sql.Length && sql[i + 1] == '\'')
                    i++; // skip the escaped quote
                else
                    inString = !inString;
            }
        }
        return inString;
    }

    private static void BindParameters(
        DbCommand cmd, string sql, IVariableStore variables, List<CellOutput> outputs)
    {
        var matches = ParamPattern.Matches(sql);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            // Skip @params that appear inside single-quoted string literals
            if (IsInsideStringLiteral(sql, match.Index))
                continue;

            var paramName = match.Groups[1].Value;
            if (!seen.Add(paramName))
                continue;

            var allVars = variables.GetAll();
            var descriptor = allVars.FirstOrDefault(v =>
                string.Equals(v.Name, paramName, StringComparison.OrdinalIgnoreCase));

            if (descriptor is null || descriptor.Value is null)
            {
                outputs.Add(new CellOutput("text/plain",
                    $"Warning: No variable '@{paramName}' found for parameter binding.",
                    IsError: false));
                continue;
            }

            var param = cmd.CreateParameter();
            param.ParameterName = $"@{paramName}";

            if (DbTypeMapper.TryMapDbType(descriptor.Type, out var dbType))
            {
                param.DbType = dbType;
                param.Value = descriptor.Value;
            }
            else
            {
                outputs.Add(new CellOutput("text/plain",
                    $"Warning: Type '{descriptor.Type.Name}' for '@{paramName}' is not a supported DbType. Passing as-is.",
                    IsError: false));
                param.Value = descriptor.Value;
            }

            cmd.Parameters.Add(param);
        }
    }

    private static async Task<SqlResultSet> ReadResultSetAsync(
        DbDataReader reader, int maxRows, CancellationToken ct)
    {
        // Read column metadata
        var columns = new List<SqlColumnMetadata>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new SqlColumnMetadata(
                reader.GetName(i),
                reader.GetDataTypeName(i),
                reader.GetFieldType(i),
                true)); // Most providers default to nullable
        }

        // Read rows
        var rows = new List<object?[]>();
        int totalCount = 0;
        bool wasTruncated = false;

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            totalCount++;
            if (rows.Count < maxRows)
            {
                var row = new object?[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }
            else
            {
                wasTruncated = true;
            }
        }

        return new SqlResultSet(columns, rows, totalCount, wasTruncated);
    }

    private static DataTable ToDataTable(SqlResultSet resultSet)
    {
        var dt = new DataTable();

        foreach (var col in resultSet.Columns)
        {
            var dc = new DataColumn(col.Name, col.ClrType);
            dc.AllowDBNull = col.AllowsNull;
            dt.Columns.Add(dc);
        }

        foreach (var row in resultSet.Rows)
        {
            var dr = dt.NewRow();
            for (int i = 0; i < row.Length; i++)
            {
                dr[i] = row[i] ?? DBNull.Value;
            }
            dt.Rows.Add(dr);
        }

        return dt;
    }

    // --- SQL Keywords ---

    internal static readonly string[] SqlKeywords = new[]
    {
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE",
        "CREATE", "ALTER", "DROP", "TABLE", "INDEX", "VIEW", "DATABASE",
        "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "CROSS", "ON",
        "GROUP", "BY", "ORDER", "ASC", "DESC", "HAVING",
        "AND", "OR", "NOT", "IN", "EXISTS", "BETWEEN", "LIKE", "IS", "NULL",
        "AS", "DISTINCT", "TOP", "LIMIT", "OFFSET", "FETCH", "NEXT",
        "UNION", "ALL", "INTERSECT", "EXCEPT",
        "CASE", "WHEN", "THEN", "ELSE", "END",
        "COUNT", "SUM", "AVG", "MIN", "MAX",
        "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION",
        "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT",
        "WITH", "RECURSIVE", "OVER", "PARTITION", "ROW_NUMBER", "RANK"
    };

    internal static readonly Dictionary<string, string> KeywordDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SELECT"] = "SELECT — Retrieves rows from one or more tables or views.",
        ["FROM"] = "FROM — Specifies the source tables or views for a query.",
        ["WHERE"] = "WHERE — Filters rows based on a condition.",
        ["INSERT"] = "INSERT — Adds new rows to a table.",
        ["INTO"] = "INTO — Specifies the target table for INSERT.",
        ["VALUES"] = "VALUES — Specifies literal values for INSERT.",
        ["UPDATE"] = "UPDATE — Modifies existing rows in a table.",
        ["SET"] = "SET — Specifies columns and values for UPDATE.",
        ["DELETE"] = "DELETE — Removes rows from a table.",
        ["CREATE"] = "CREATE — Creates a new database object (table, view, index, etc.).",
        ["ALTER"] = "ALTER — Modifies an existing database object.",
        ["DROP"] = "DROP — Removes a database object.",
        ["TABLE"] = "TABLE — Refers to a database table.",
        ["JOIN"] = "JOIN — Combines rows from two or more tables based on a related column.",
        ["INNER"] = "INNER — Returns rows that have matching values in both tables.",
        ["LEFT"] = "LEFT — Returns all rows from the left table and matching rows from the right.",
        ["RIGHT"] = "RIGHT — Returns all rows from the right table and matching rows from the left.",
        ["GROUP"] = "GROUP BY — Groups rows sharing a property for aggregate functions.",
        ["ORDER"] = "ORDER BY — Sorts the result set by one or more columns.",
        ["HAVING"] = "HAVING — Filters groups created by GROUP BY.",
        ["AND"] = "AND — Logical AND operator; both conditions must be true.",
        ["OR"] = "OR — Logical OR operator; either condition must be true.",
        ["NOT"] = "NOT — Logical NOT operator; negates a condition.",
        ["IN"] = "IN — Tests whether a value matches any value in a list or subquery.",
        ["EXISTS"] = "EXISTS — Tests for the existence of rows in a subquery.",
        ["BETWEEN"] = "BETWEEN — Tests whether a value is within a range.",
        ["LIKE"] = "LIKE — Pattern matching with wildcards (% and _).",
        ["NULL"] = "NULL — Represents a missing or unknown value.",
        ["DISTINCT"] = "DISTINCT — Removes duplicate rows from the result set.",
        ["UNION"] = "UNION — Combines result sets of two or more SELECT statements.",
        ["CASE"] = "CASE — Conditional expression; returns a value based on conditions.",
        ["COUNT"] = "COUNT — Aggregate function that returns the number of rows.",
        ["SUM"] = "SUM — Aggregate function that returns the total of a numeric column.",
        ["AVG"] = "AVG — Aggregate function that returns the average of a numeric column.",
        ["MIN"] = "MIN — Aggregate function that returns the minimum value.",
        ["MAX"] = "MAX — Aggregate function that returns the maximum value.",
    };
}
