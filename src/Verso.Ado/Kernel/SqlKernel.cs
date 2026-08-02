using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using Verso.Abstractions;
using Verso.Ado.Formatters;
using Verso.Ado.Helpers;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Ado.Localization;
using Verso.Ado.Resources;

namespace Verso.Ado.Kernel;

/// <summary>
/// Language kernel for executing SQL against ADO.NET database connections.
/// Results are published as <see cref="DataTable"/> to the variable store.
/// Accessed through <see cref="CellType.SqlCellType"/>; not independently registered.
/// </summary>
public sealed class SqlKernel : ILanguageKernel
{
    private readonly SemaphoreSlim _executionLock = new(1, 1);

    internal const int DefaultMaxFetchRows = 10_000;
    private const int DefaultDisplayPageSize = 50;

    private IVariableStore? _lastVariableStore;
    private readonly SchemaCache _schemaCache = SchemaCache.Instance;

    // --- IExtension ---
    public string ExtensionId => "verso.ado.kernel.sql";
    string IExtension.Name => "SQL Kernel";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.Kernel_Description;

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

        // Surface a directive typed with a space after the dashes (e.g. "-- connection
        // Primary"), which SQL reads as a comment and silently ignores.
        var directiveHint = SqlDirectives.DetectMisusedDirective(code);
        if (directiveHint is not null)
            outputs.Add(new CellOutput("text/plain", directiveHint, IsError: false));

        if (string.IsNullOrWhiteSpace(sqlCode))
        {
            outputs.Add(new CellOutput("text/plain", Strings.Run_NoSql, IsError: true));
            return outputs;
        }

        // Resolve connection
        var connInfo = ConnectionResolver.Resolve(directives.ConnectionName, context.Variables);
        if (connInfo is null)
        {
            outputs.Add(new CellOutput("text/plain",
                Strings.Run_NoConnection, IsError: true));
            return outputs;
        }

        if (connInfo.Connection is null || connInfo.Connection.State != ConnectionState.Open)
        {
            outputs.Add(new CellOutput("text/plain",
                string.Format(Strings.Run_ConnectionClosed, connInfo.Name), IsError: true));
            return outputs;
        }

        // Resolve dialect for parameter scanning and GO batch handling
        var dialect = SqlDialectResolver.FromProviderName(connInfo.ProviderName);
        bool isSqlServer = dialect == SqlDialect.SqlServer;

        // Split statements. On SQL Server a semicolon terminates a statement but does
        // not start a new batch, so the cell is split only on GO and each batch is sent
        // as a single command — this preserves T-SQL variable scope across statements
        // (e.g. a DECLARE whose locals are read by a later EXEC). Other dialects, which
        // execute one statement per command, keep splitting on semicolons.
        var statements = SqlStatementSplitter.Split(
            sqlCode, handleGoBatches: isSqlServer, splitOnSemicolon: !isSqlServer);

        int maxRows = directives.PageSize ?? DefaultMaxFetchRows;
        int displayPageSize = DefaultDisplayPageSize;

        // Resolve the command timeout: a per-cell `--timeout` directive wins, then the
        // connection default set on `#!sql-connect --command-timeout`, otherwise the
        // provider default (commonly 30s) is left untouched. A value of 0 means no limit.
        int? commandTimeout = directives.CommandTimeout ?? connInfo.CommandTimeout;

        await _executionLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            // A database says things that do not arrive with the result set: SQL Server's PRINT,
            // PostgreSQL's RAISE NOTICE, a procedure reporting how far it has got. They collect
            // into one block that is revised as more arrive, so a chatty procedure reads as a
            // running log rather than a column of separate outputs. The subscription lasts only as
            // long as this cell, because the connection outlives it.
            var messages = new StringBuilder();
            var messageLock = new object();
            using var messageListener = ProviderMessageListener.Subscribe(
                connInfo.Connection,
                text =>
                {
                    string current;
                    lock (messageLock)
                    {
                        messages.AppendLine(text);
                        current = messages.ToString();
                    }

                    ReportMessages(current, context);
                });

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
                if (commandTimeout is int timeoutSeconds)
                    cmd.CommandTimeout = timeoutSeconds;

                // Bind parameters
                BindParameters(cmd, statement, dialect, context.Variables, outputs);

                using var reader = await cmd.ExecuteReaderAsync(context.CancellationToken).ConfigureAwait(false);

                sw.Stop();

                // A single command may carry several result sets — multiple SELECTs in
                // one SQL Server batch, or a stored procedure that returns more than one.
                // Walk every result set so each is displayed.
                bool anyResultSet = false;
                do
                {
                    if (reader.FieldCount > 0)
                    {
                        anyResultSet = true;

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
                }
                while (await reader.NextResultAsync(context.CancellationToken).ConfigureAwait(false));

                // RecordsAffected is cumulative for the whole command and reliable only
                // once the reader is fully consumed. Summarize the non-query work for a
                // batch that produced no result sets to display.
                if (!anyResultSet)
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
            outputs.Add(new CellOutput("text/plain", string.Format(Strings.Run_Failed, ex.Message), IsError: true));
        }
        finally
        {
            _executionLock.Release();
        }

        return outputs;
    }

    /// <summary>
    /// Identifies the block the server's messages accumulate in, so each new one revises it rather
    /// than adding another.
    /// </summary>
    private const string MessageBlockId = "sql-messages";

    /// <summary>
    /// Shows the messages received so far. Carries no output channel: this is not text a kernel
    /// wrote to one of its own streams, it is what the server said, so it reads as ordinary output.
    /// </summary>
    private static void ReportMessages(string text, IExecutionContext context)
    {
        var trimmed = text.TrimEnd('\r', '\n');
        if (trimmed.Length == 0)
            return;

        var output = new CellOutput("text/plain", trimmed);

        try
        {
            context.UpdateOutputAsync(MessageBlockId, output).GetAwaiter().GetResult();
        }
        catch (NotSupportedException)
        {
            // A host that cannot revise still shows the messages, just one block per message.
            context.WriteOutputAsync(output).GetAwaiter().GetResult();
        }
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

        // Resolve connection (and dialect for parameter scanning)
        SqlConnectionInfo? connInfo = null;
        var dialect = SqlDialect.Unknown;
        if (_lastVariableStore is not null)
        {
            connInfo = ConnectionResolver.Resolve(directives.ConnectionName, _lastVariableStore);
            if (connInfo is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    directives.ConnectionName is not null
                        ? $"Connection '{directives.ConnectionName}' not found. Use #!sql-connect to establish a connection."
                        : "No database connection. Use #!sql-connect to establish a connection.",
                    0, 0, 0, 0));
            }
            else
            {
                dialect = SqlDialectResolver.FromProviderName(connInfo.ProviderName);
            }
        }
        else
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "No database connection. Use #!sql-connect to establish a connection.",
                0, 0, 0, 0));
        }

        // Scan for unresolved parameters in the post-directive-strip SQL,
        // then translate offsets back to the original cell's line numbering.
        int lineOffset = sqlCode.Length < code.Length ? 1 : 0;
        char prefix = dialect.ParameterPrefix();

        var localNames = SqlLocalScopeAnalyzer.FindLocalNames(sqlCode, dialect);
        var references = SqlParameterScanner.Scan(sqlCode, dialect);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references)
        {
            if (localNames.Contains(reference.Name))
                continue;
            if (!seen.Add(reference.Name))
                continue;

            bool resolved = false;
            if (_lastVariableStore is not null)
            {
                var allVars = _lastVariableStore.GetAll();
                resolved = allVars.Any(v =>
                    string.Equals(v.Name, reference.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (!resolved)
            {
                var (startLine, startCol) = OffsetToLineCol(sqlCode, reference.Offset);
                var (endLine, endCol) = OffsetToLineCol(sqlCode, reference.Offset + reference.Length);

                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Unresolved parameter '{prefix}{reference.Name}'. No matching variable found in the variable store.",
                    startLine + lineOffset, startCol, endLine + lineOffset, endCol));
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
        if (DescribeKeyword(lookup) is { } kwDescription)
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

    private static void BindParameters(
        DbCommand cmd, string sql, SqlDialect dialect,
        IVariableStore variables, List<CellOutput> outputs)
    {
        var localNames = SqlLocalScopeAnalyzer.FindLocalNames(sql, dialect);
        var references = SqlParameterScanner.Scan(sql, dialect);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        char prefix = dialect.ParameterPrefix();

        ConfigureBindingForDialect(cmd, dialect);

        foreach (var reference in references)
        {
            // Skip names introduced by the SQL itself (T-SQL DECLARE / proc params, etc.)
            if (localNames.Contains(reference.Name))
                continue;

            if (!seen.Add(reference.Name))
                continue;

            var allVars = variables.GetAll();
            var descriptor = allVars.FirstOrDefault(v =>
                string.Equals(v.Name, reference.Name, StringComparison.OrdinalIgnoreCase));

            if (descriptor is null || descriptor.Value is null)
            {
                // No matching notebook variable. The token is left in the SQL untouched
                // for the database to interpret — in T-SQL an unmatched @name is almost
                // always a native local (DECLARE) or a stored-procedure parameter name
                // (EXEC proc @p = …), not a notebook binding. Warning on every such token
                // produced alarming noise on valid SQL, so unmatched tokens are skipped
                // silently; a genuinely missing reference surfaces as a clear database error.
                continue;
            }

            var param = cmd.CreateParameter();
            param.ParameterName = $"{prefix}{reference.Name}";

            if (DbTypeMapper.TryMapDbType(descriptor.Type, out var dbType))
            {
                param.DbType = dbType;
                param.Value = descriptor.Value;
            }
            else
            {
                outputs.Add(new CellOutput("text/plain",
                    CellText.Warning(string.Format(Strings.Run_UnsupportedParameterType,
                        descriptor.Type.Name, $"{prefix}{reference.Name}")),
                    IsError: false));
                param.Value = descriptor.Value;
            }

            cmd.Parameters.Add(param);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?>
        _bindByNameProperties = new();

    /// <summary>
    /// Applies dialect-specific command configuration before parameters are added.
    /// Oracle's ADO.NET provider binds parameters positionally by default; because
    /// the binder adds one parameter per distinct name, named binding must be
    /// enabled so a query that reuses a bind or lists parameters out of source
    /// order resolves correctly. The provider is resolved at runtime with no
    /// compile-time reference, so the well-known <c>BindByName</c> property is set
    /// reflectively when the concrete command exposes it.
    /// </summary>
    private static void ConfigureBindingForDialect(DbCommand cmd, SqlDialect dialect)
    {
        if (dialect != SqlDialect.Oracle)
            return;

        var prop = _bindByNameProperties.GetOrAdd(
            cmd.GetType(),
            static t => t.GetProperty("BindByName", typeof(bool)));

        if (prop is null || !prop.CanWrite)
            return;

        try
        {
            prop.SetValue(cmd, true);
        }
        catch
        {
            // Provider does not honor BindByName; fall back to its default behavior.
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

    /// <summary>What a SQL keyword does, or <c>null</c> for a word that is not one.</summary>
    /// <remarks>
    /// Looked up rather than held in a table, because a table built once would answer in
    /// whichever language happened to be set when the kernel first loaded.
    /// </remarks>
    internal static string? DescribeKeyword(string word) => word.ToUpperInvariant() switch
    {
        "SELECT" => Strings.Completion_Keyword_Select,
        "FROM" => Strings.Completion_Keyword_From,
        "WHERE" => Strings.Completion_Keyword_Where,
        "INSERT" => Strings.Completion_Keyword_Insert,
        "INTO" => Strings.Completion_Keyword_Into,
        "VALUES" => Strings.Completion_Keyword_Values,
        "UPDATE" => Strings.Completion_Keyword_Update,
        "SET" => Strings.Completion_Keyword_Set,
        "DELETE" => Strings.Completion_Keyword_Delete,
        "CREATE" => Strings.Completion_Keyword_Create,
        "ALTER" => Strings.Completion_Keyword_Alter,
        "DROP" => Strings.Completion_Keyword_Drop,
        "TABLE" => Strings.Completion_Keyword_Table,
        "JOIN" => Strings.Completion_Keyword_Join,
        "INNER" => Strings.Completion_Keyword_Inner,
        "LEFT" => Strings.Completion_Keyword_Left,
        "RIGHT" => Strings.Completion_Keyword_Right,
        "GROUP" => Strings.Completion_Keyword_Group,
        "ORDER" => Strings.Completion_Keyword_Order,
        "HAVING" => Strings.Completion_Keyword_Having,
        "AND" => Strings.Completion_Keyword_And,
        "OR" => Strings.Completion_Keyword_Or,
        "NOT" => Strings.Completion_Keyword_Not,
        "IN" => Strings.Completion_Keyword_In,
        "EXISTS" => Strings.Completion_Keyword_Exists,
        "BETWEEN" => Strings.Completion_Keyword_Between,
        "LIKE" => Strings.Completion_Keyword_Like,
        "NULL" => Strings.Completion_Keyword_Null,
        "DISTINCT" => Strings.Completion_Keyword_Distinct,
        "UNION" => Strings.Completion_Keyword_Union,
        "CASE" => Strings.Completion_Keyword_Case,
        "COUNT" => Strings.Completion_Keyword_Count,
        "SUM" => Strings.Completion_Keyword_Sum,
        "AVG" => Strings.Completion_Keyword_Avg,
        "MIN" => Strings.Completion_Keyword_Min,
        "MAX" => Strings.Completion_Keyword_Max,
        _ => null,
    };
}
