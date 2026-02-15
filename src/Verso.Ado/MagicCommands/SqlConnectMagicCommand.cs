using System.Data.Common;
using Verso.Abstractions;
using Verso.Ado.Helpers;
using Verso.Ado.Models;

namespace Verso.Ado.MagicCommands;

/// <summary>
/// <c>#!sql-connect --name db --connection-string "..." [--provider ...] [--default]</c>
/// — establishes a database connection and stores it in the variable store.
/// </summary>
[VersoExtension]
public sealed class SqlConnectMagicCommand : IMagicCommand
{
    internal const string ConnectionsStoreKey = "__verso_ado_connections";
    internal const string DefaultConnectionStoreKey = "__verso_ado_default";

    // --- IExtension ---
    public string ExtensionId => "verso.ado.magic.sql-connect";
    string IExtension.Name => "SQL Connect Magic Command";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string Description => "Establishes a database connection for SQL cells.";

    // --- IMagicCommand ---
    public string Name => "sql-connect";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("name", "A friendly name for this connection.", typeof(string), IsRequired: true),
        new ParameterDefinition("connection-string", "The ADO.NET connection string.", typeof(string), IsRequired: true),
        new ParameterDefinition("provider", "The DbProviderFactory invariant name.", typeof(string)),
        new ParameterDefinition("default", "Set this connection as the default.", typeof(bool)),
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        context.SuppressExecution = true;

        var args = ArgumentParser.Parse(arguments);

        // Validate required parameters
        if (!args.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "Error: --name is required. Usage: #!sql-connect --name <name> --connection-string <cs>",
                IsError: true)).ConfigureAwait(false);
            return;
        }

        if (!args.TryGetValue("connection-string", out var rawCs) || string.IsNullOrWhiteSpace(rawCs))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", "Error: --connection-string is required. Usage: #!sql-connect --name <name> --connection-string <cs>",
                IsError: true)).ConfigureAwait(false);
            return;
        }

        // Resolve credentials
        var (resolvedCs, credError) = CredentialResolver.ResolveConnectionString(rawCs);
        if (credError is not null)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", $"Error resolving connection string: {credError}", IsError: true))
                .ConfigureAwait(false);
            return;
        }

        // Discover provider (pass NuGet assembly paths so unloaded packages can be found)
        args.TryGetValue("provider", out var explicitProvider);
        context.Variables.TryGet<List<string>>("__verso_nuget_assemblies", out var nugetPaths);
        var (factory, providerName, providerError) = ProviderDiscovery.Discover(resolvedCs!, explicitProvider, nugetPaths);
        if (providerError is not null)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", $"Error: {providerError}", IsError: true)).ConfigureAwait(false);
            return;
        }

        // Create and open connection
        DbConnection connection;
        try
        {
            connection = factory!.CreateConnection()!;
            connection.ConnectionString = resolvedCs!;
            await connection.OpenAsync(context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", $"Error opening connection: {ex.Message}", IsError: true))
                .ConfigureAwait(false);
            return;
        }

        // Store connection info
        var connInfo = new SqlConnectionInfo(name, resolvedCs!, providerName, connection);

        var connections = context.Variables.Get<Dictionary<string, SqlConnectionInfo>>(ConnectionsStoreKey)
            ?? new Dictionary<string, SqlConnectionInfo>(StringComparer.OrdinalIgnoreCase);

        connections[name] = connInfo;
        context.Variables.Set(ConnectionsStoreKey, connections);

        // Set as default if --default flag or first connection
        bool isDefault = args.ContainsKey("default") || connections.Count == 1;
        if (isDefault)
        {
            context.Variables.Set(DefaultConnectionStoreKey, name);
        }

        var redacted = CredentialResolver.RedactConnectionString(resolvedCs!);
        var dbName = connection.Database;
        var defaultLabel = isDefault ? " (default)" : "";

        await context.WriteOutputAsync(new CellOutput(
            "text/plain",
            $"Connected '{name}'{defaultLabel} using {providerName ?? "unknown"}" +
            (!string.IsNullOrEmpty(dbName) ? $" — database: {dbName}" : "") +
            $"\n  Connection string: {redacted}"))
            .ConfigureAwait(false);
    }
}
