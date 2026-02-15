using Verso.Abstractions;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;

namespace Verso.Ado.Helpers;

/// <summary>
/// Shared helper for resolving the active SQL connection from the variable store.
/// </summary>
internal static class ConnectionResolver
{
    /// <summary>
    /// Resolves a <see cref="SqlConnectionInfo"/> by name (or default) from the variable store.
    /// </summary>
    /// <param name="connectionName">Explicit connection name, or <c>null</c> to use the default.</param>
    /// <param name="variables">The variable store containing connection state.</param>
    /// <returns>The resolved connection info, or <c>null</c> if not found.</returns>
    internal static SqlConnectionInfo? Resolve(string? connectionName, IVariableStore variables)
    {
        var connections = variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey);

        if (connections is null || connections.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(connectionName))
        {
            connectionName = variables.Get<string>(SqlConnectMagicCommand.DefaultConnectionStoreKey);
        }

        if (connectionName is not null && connections.TryGetValue(connectionName, out var connInfo))
            return connInfo;

        return null;
    }

    /// <summary>
    /// Returns the name of the default connection, or <c>null</c> if none is set.
    /// </summary>
    internal static string? GetDefaultConnectionName(IVariableStore variables)
    {
        return variables.Get<string>(SqlConnectMagicCommand.DefaultConnectionStoreKey);
    }

    /// <summary>
    /// Returns all connection names currently stored.
    /// </summary>
    internal static IReadOnlyList<string> GetConnectionNames(IVariableStore variables)
    {
        var connections = variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey);

        return connections?.Keys.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
    }
}
