namespace Verso.Ado.Helpers;

/// <summary>
/// Maps ADO.NET provider invariant names to the corresponding <see cref="SqlDialect"/>.
/// </summary>
internal static class SqlDialectResolver
{
    /// <summary>
    /// Returns the <see cref="SqlDialect"/> for the given provider invariant name,
    /// or <see cref="SqlDialect.Unknown"/> if the name is null/empty or unrecognized.
    /// </summary>
    internal static SqlDialect FromProviderName(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return SqlDialect.Unknown;

        // Order matters: MySql.Data.MySqlClient contains both "MySql" and
        // "SqlClient", so MySql must be checked before SqlClient.
        if (providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.MySql;
        if (providerName.Contains("SqlClient", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.SqlServer;
        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.Sqlite;
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.Postgres;
        if (providerName.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
            return SqlDialect.Oracle;

        return SqlDialect.Unknown;
    }
}
