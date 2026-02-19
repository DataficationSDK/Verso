using Verso.Abstractions;
using Verso.Ado.Helpers;
using Verso.Ado.Models;

namespace Verso.Ado.MagicCommands;

/// <summary>
/// <c>#!sql-disconnect [--name db]</c> — closes and removes a database connection.
/// If no name is specified, the default connection is disconnected.
/// </summary>
[VersoExtension]
public sealed class SqlDisconnectMagicCommand : IMagicCommand
{
    // --- IExtension ---
    public string ExtensionId => "verso.ado.magic.sql-disconnect";
    string IExtension.Name => "SQL Disconnect Magic Command";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string Description => "Closes and removes a database connection.";

    // --- IMagicCommand ---
    public string Name => "sql-disconnect";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("name", "The connection name to disconnect. Defaults to the default connection.", typeof(string)),
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        var args = ArgumentParser.Parse(arguments);

        // Resolve connection name
        args.TryGetValue("name", out var connectionName);
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            connectionName = context.Variables.Get<string>(SqlConnectMagicCommand.DefaultConnectionStoreKey);
        }

        if (string.IsNullOrWhiteSpace(connectionName))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "Error: No connection name specified and no default connection is set.",
                IsError: true)).ConfigureAwait(false);
            return;
        }

        var connections = context.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey);

        if (connections is null || !connections.TryGetValue(connectionName, out var connInfo))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", $"Error: Connection '{connectionName}' not found.", IsError: true))
                .ConfigureAwait(false);
            return;
        }

        // Close and dispose the connection
        if (connInfo.Connection is not null)
        {
            try
            {
                await connInfo.Connection.CloseAsync().ConfigureAwait(false);
                await connInfo.Connection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort close
            }
        }

        connections.Remove(connectionName);
        context.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);

        // Update default if we disconnected the default connection
        var currentDefault = context.Variables.Get<string>(SqlConnectMagicCommand.DefaultConnectionStoreKey);
        if (string.Equals(currentDefault, connectionName, StringComparison.OrdinalIgnoreCase))
        {
            if (connections.Count > 0)
            {
                var newDefault = connections.Keys.First();
                context.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, newDefault);
            }
            else
            {
                context.Variables.Remove(SqlConnectMagicCommand.DefaultConnectionStoreKey);
            }
        }

        await context.WriteOutputAsync(new CellOutput(
            "text/plain", $"Disconnected '{connectionName}'.")).ConfigureAwait(false);
    }
}
