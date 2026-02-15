using System.Collections.Concurrent;
using System.Data.Common;

namespace Verso.Ado.Kernel;

internal sealed record TableInfo(string Name, string? Schema, string TableType);

internal sealed record ColumnInfo(
    string Name,
    string DataType,
    bool IsNullable,
    string? DefaultValue,
    bool IsPrimaryKey,
    int OrdinalPosition);

internal sealed record ForeignKeyInfo(
    string ConstraintName,
    string FromTable,
    string FromColumn,
    string ToTable,
    string ToColumn);

internal sealed record SchemaCacheEntry(
    List<TableInfo> Tables,
    Dictionary<string, List<ColumnInfo>> Columns,
    Dictionary<string, List<ForeignKeyInfo>> ForeignKeys,
    DateTimeOffset LoadedAt);

/// <summary>
/// Queries and caches database schema metadata (tables, views, columns) for IntelliSense.
/// </summary>
internal sealed class SchemaCache
{
    internal static readonly SchemaCache Instance = new();

    private readonly ConcurrentDictionary<string, SchemaCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl;

    internal SchemaCache(int ttlSeconds = 300)
    {
        _ttl = TimeSpan.FromSeconds(ttlSeconds);
    }

    internal async Task<SchemaCacheEntry> GetOrRefreshAsync(
        string connectionName, DbConnection connection, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(connectionName, out var entry) &&
            DateTimeOffset.UtcNow - entry.LoadedAt < _ttl)
        {
            return entry;
        }

        var newEntry = await LoadSchemaAsync(connection, ct).ConfigureAwait(false);
        _cache[connectionName] = newEntry;
        return newEntry;
    }

    internal void Invalidate(string connectionName)
    {
        _cache.TryRemove(connectionName, out _);
    }

    internal void InvalidateAll()
    {
        _cache.Clear();
    }

    internal bool TryGetCached(string connectionName, out SchemaCacheEntry? entry)
    {
        if (_cache.TryGetValue(connectionName, out entry) &&
            DateTimeOffset.UtcNow - entry.LoadedAt < _ttl)
        {
            return true;
        }

        entry = null;
        return false;
    }

    private static async Task<SchemaCacheEntry> LoadSchemaAsync(DbConnection connection, CancellationToken ct)
    {
        bool isSqlite = IsSqlite(connection);

        var tables = isSqlite
            ? await LoadSqliteTablesAsync(connection, ct).ConfigureAwait(false)
            : await LoadInformationSchemaTablesAsync(connection, ct).ConfigureAwait(false);

        var columns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
        var foreignKeys = new Dictionary<string, List<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var cols = isSqlite
                ? await LoadSqliteColumnsAsync(connection, table.Name, ct).ConfigureAwait(false)
                : await LoadInformationSchemaColumnsAsync(connection, table.Name, table.Schema, ct).ConfigureAwait(false);
            columns[table.Name] = cols;

            var fks = isSqlite
                ? await LoadSqliteForeignKeysAsync(connection, table.Name, ct).ConfigureAwait(false)
                : await LoadInformationSchemaForeignKeysAsync(connection, table.Name, table.Schema, ct).ConfigureAwait(false);
            if (fks.Count > 0)
                foreignKeys[table.Name] = fks;
        }

        return new SchemaCacheEntry(tables, columns, foreignKeys, DateTimeOffset.UtcNow);
    }

    private static bool IsSqlite(DbConnection connection)
    {
        var typeName = connection.GetType().FullName ?? "";
        return typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<TableInfo>> LoadSqliteTablesAsync(DbConnection connection, CancellationToken ct)
    {
        var tables = new List<TableInfo>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, type FROM sqlite_master WHERE type IN ('table', 'view') ORDER BY name";

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            tables.Add(new TableInfo(name, null, type.Equals("table", StringComparison.OrdinalIgnoreCase) ? "TABLE" : "VIEW"));
        }

        return tables;
    }

    private static async Task<List<ColumnInfo>> LoadSqliteColumnsAsync(
        DbConnection connection, string tableName, CancellationToken ct)
    {
        var columns = new List<ColumnInfo>();
        using var cmd = connection.CreateCommand();
        // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
        cmd.CommandText = $"PRAGMA table_info([{tableName}])";

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var ordinal = reader.GetInt32(0);
            var name = reader.GetString(1);
            var dataType = reader.IsDBNull(2) ? "TEXT" : reader.GetString(2);
            var notNull = reader.GetInt32(3) != 0;
            var defaultValue = reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString();
            var isPk = reader.GetInt32(5) != 0;

            columns.Add(new ColumnInfo(name, dataType, !notNull, defaultValue, isPk, ordinal));
        }

        return columns;
    }

    private static async Task<List<TableInfo>> LoadInformationSchemaTablesAsync(
        DbConnection connection, CancellationToken ct)
    {
        var tables = new List<TableInfo>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TABLE_NAME, TABLE_SCHEMA, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_SCHEMA, TABLE_NAME";

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var schema = reader.IsDBNull(1) ? null : reader.GetString(1);
            var tableType = reader.IsDBNull(2) ? "TABLE" : reader.GetString(2);
            tables.Add(new TableInfo(name, schema, tableType));
        }

        return tables;
    }

    private static async Task<List<ForeignKeyInfo>> LoadSqliteForeignKeysAsync(
        DbConnection connection, string tableName, CancellationToken ct)
    {
        var fks = new List<ForeignKeyInfo>();
        try
        {
            using var cmd = connection.CreateCommand();
            // PRAGMA foreign_key_list returns: id, seq, table, from, to, on_update, on_delete, match
            cmd.CommandText = $"PRAGMA foreign_key_list([{tableName}])";

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetInt32(0);
                var toTable = reader.GetString(2);
                var fromColumn = reader.GetString(3);
                var toColumn = reader.IsDBNull(4) ? "Id" : reader.GetString(4);

                fks.Add(new ForeignKeyInfo(
                    $"FK_{tableName}_{toTable}_{id}",
                    tableName,
                    fromColumn,
                    toTable,
                    toColumn));
            }
        }
        catch (DbException)
        {
            // foreign_key_list may not be available
        }

        return fks;
    }

    private static async Task<List<ForeignKeyInfo>> LoadInformationSchemaForeignKeysAsync(
        DbConnection connection, string tableName, string? schema, CancellationToken ct)
    {
        var fks = new List<ForeignKeyInfo>();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = schema is not null
                ? @"SELECT rc.CONSTRAINT_NAME, kcu.COLUMN_NAME, kcu2.TABLE_NAME, kcu2.COLUMN_NAME
                    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                      ON rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME AND rc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu2
                      ON rc.UNIQUE_CONSTRAINT_NAME = kcu2.CONSTRAINT_NAME AND rc.UNIQUE_CONSTRAINT_SCHEMA = kcu2.CONSTRAINT_SCHEMA
                    WHERE kcu.TABLE_NAME = '" + tableName + "' AND kcu.TABLE_SCHEMA = '" + schema + "'"
                : @"SELECT rc.CONSTRAINT_NAME, kcu.COLUMN_NAME, kcu2.TABLE_NAME, kcu2.COLUMN_NAME
                    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                      ON rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu2
                      ON rc.UNIQUE_CONSTRAINT_NAME = kcu2.CONSTRAINT_NAME
                    WHERE kcu.TABLE_NAME = '" + tableName + "'";

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                fks.Add(new ForeignKeyInfo(
                    reader.GetString(0),
                    tableName,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
        }
        catch (DbException)
        {
            // REFERENTIAL_CONSTRAINTS may not be available on all providers
        }

        return fks;
    }

    private static async Task<List<ColumnInfo>> LoadInformationSchemaColumnsAsync(
        DbConnection connection, string tableName, string? schema, CancellationToken ct)
    {
        var columns = new List<ColumnInfo>();

        // Load primary key columns first
        var pkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var pkCmd = connection.CreateCommand();
            pkCmd.CommandText = schema is not null
                ? $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_NAME = '{tableName}' AND TABLE_SCHEMA = '{schema}'"
                : $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_NAME = '{tableName}'";
            using var pkReader = await pkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await pkReader.ReadAsync(ct).ConfigureAwait(false))
            {
                pkColumns.Add(pkReader.GetString(0));
            }
        }
        catch (DbException)
        {
            // KEY_COLUMN_USAGE may not be available on all providers
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = schema is not null
            ? $"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT, ORDINAL_POSITION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}' AND TABLE_SCHEMA = '{schema}' ORDER BY ORDINAL_POSITION"
            : $"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT, ORDINAL_POSITION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}' ORDER BY ORDINAL_POSITION";

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var dataType = reader.IsDBNull(1) ? "unknown" : reader.GetString(1);
            var isNullableStr = reader.IsDBNull(2) ? "YES" : reader.GetString(2);
            var defaultValue = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString();
            var ordinal = reader.GetInt32(4);

            columns.Add(new ColumnInfo(
                name,
                dataType,
                isNullableStr.Equals("YES", StringComparison.OrdinalIgnoreCase),
                defaultValue,
                pkColumns.Contains(name),
                ordinal));
        }

        return columns;
    }
}
