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
        var outputs = new List<CellOutput>();

        // Parse directives
        var (directives, sqlCode) = SqlDirectives.Parse(code);

        if (string.IsNullOrWhiteSpace(sqlCode))
        {
            outputs.Add(new CellOutput("text/plain", "No SQL to execute.", IsError: true));
            return outputs;
        }

        // Resolve connection
        var connInfo = ResolveConnection(directives, context.Variables);
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
                    if (!directives.NoDisplay && affected >= 0)
                    {
                        var html = ResultSetFormatter.FormatNonQueryHtml(affected, sw.ElapsedMilliseconds, context.Theme);
                        outputs.Add(new CellOutput("text/html", html));
                    }
                }
            }

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

    public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
        => Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
        => Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
        => Task.FromResult<HoverInfo?>(null);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // --- Private helpers ---

    private static SqlConnectionInfo? ResolveConnection(SqlDirectives directives, IVariableStore variables)
    {
        var connections = variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey);

        if (connections is null || connections.Count == 0)
            return null;

        string? connectionName = directives.ConnectionName;
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            connectionName = variables.Get<string>(SqlConnectMagicCommand.DefaultConnectionStoreKey);
        }

        if (connectionName is not null && connections.TryGetValue(connectionName, out var connInfo))
            return connInfo;

        return null;
    }

    private static void BindParameters(
        DbCommand cmd, string sql, IVariableStore variables, List<CellOutput> outputs)
    {
        var matches = ParamPattern.Matches(sql);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
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
}
