using System.Data.Common;
using Verso.Abstractions;
using Verso.Ado.Helpers;
using Verso.Ado.Models;
using Verso.Ado.Localization;
using Verso.Ado.Resources;

namespace Verso.Ado.MagicCommands;

/// <summary>
/// <c>#!sql-connect --name db --connection-string "..." [--provider ...] [--default] [--command-timeout N]</c>
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
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string Description => Strings.Magic_Connect_Description;

    // --- IMagicCommand ---
    public string Name => "sql-connect";

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("name", Strings.Magic_Connect_Param_Name, typeof(string), IsRequired: true),
        new ParameterDefinition("connection-string", Strings.Magic_Connect_Param_ConnectionString, typeof(string), IsRequired: true),
        new ParameterDefinition("provider", Strings.Magic_Connect_Param_Provider, typeof(string)),
        new ParameterDefinition("default", Strings.Magic_Connect_Param_Default, typeof(bool)),
        new ParameterDefinition("command-timeout", Strings.Magic_Connect_Param_CommandTimeout, typeof(int)),
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
                "text/plain", CellText.Error(Strings.Magic_Connect_NameRequired),
                IsError: true)).ConfigureAwait(false);
            return;
        }

        if (!args.TryGetValue("connection-string", out var rawCs) || string.IsNullOrWhiteSpace(rawCs))
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", CellText.Error(Strings.Magic_Connect_ConnectionStringRequired),
                IsError: true)).ConfigureAwait(false);
            return;
        }

        // Parse optional command timeout (seconds). 0 means no limit; negative is invalid.
        int? commandTimeout = null;
        if (args.TryGetValue("command-timeout", out var rawTimeout) && !string.IsNullOrWhiteSpace(rawTimeout))
        {
            if (!int.TryParse(rawTimeout, out var timeoutValue) || timeoutValue < 0)
            {
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain", CellText.Error(Strings.Magic_Connect_TimeoutInvalid),
                    IsError: true)).ConfigureAwait(false);
                return;
            }

            commandTimeout = timeoutValue;
        }

        // Resolve credentials ($env:, $var:, $secret: tokens)
        var (resolvedCs, credError) = PlaceholderResolver.ResolveConnectionString(rawCs, context.Variables);
        if (credError is not null)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", string.Format(Strings.Magic_Connect_CredentialFailed, credError), IsError: true))
                .ConfigureAwait(false);
            return;
        }

        // Resolve provider placeholder ($var:)
        args.TryGetValue("provider", out var explicitProvider);
        string? resolvedProvider = explicitProvider;
        if (!string.IsNullOrWhiteSpace(explicitProvider))
        {
            var (providerValue, providerResolveError) = PlaceholderResolver.ResolveVariable(
                explicitProvider,
                context.Variables,
                "provider");

            if (providerResolveError is not null)
            {
                await context.WriteOutputAsync(new CellOutput(
                    "text/plain", string.Format(Strings.Magic_Connect_ProviderResolveFailed, providerResolveError), IsError: true))
                    .ConfigureAwait(false);
                return;
            }

            resolvedProvider = providerValue;
        }

        // Discover provider (pass NuGet assembly paths so unloaded packages can be found)
        context.Variables.TryGet<List<string>>("__verso_nuget_assemblies", out var nugetPaths);
        var (factory, providerName, providerError) = ProviderDiscovery.Discover(resolvedCs!, resolvedProvider, nugetPaths);

        // If discovery failed and an explicit provider was given, attempt to auto-resolve
        // it as a NuGet package — but only if the provider's DLL isn't already in the
        // assembly path list. For most ADO.NET providers the invariant name, package id,
        // and assembly name are identical (Microsoft.Data.SqlClient, Microsoft.Data.Sqlite,
        // Npgsql, MySqlConnector, ...), so users shouldn't have to run a separate
        // `#r "nuget:..."` cell first. Where they diverge — notably Oracle, whose package on
        // modern .NET is Oracle.ManagedDataAccess.Core and whose assembly is
        // Oracle.ManagedDataAccess.dll — ProviderDiscovery maps the invariant name to the
        // right package and assembly. If the DLL IS already in the path list, discovery
        // failed for a different reason (e.g. type initializer exception) and re-downloading
        // won't help.
        var providerAlreadyLoaded = nugetPaths is { Count: > 0 }
            && !string.IsNullOrWhiteSpace(resolvedProvider)
            && nugetPaths.Any(p =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(p),
                    ProviderDiscovery.GetAssemblyName(resolvedProvider!),
                    StringComparison.OrdinalIgnoreCase));

        if (factory is null && !string.IsNullOrWhiteSpace(resolvedProvider) && !providerAlreadyLoaded)
        {
            var nugetCommand = context.ExtensionHost.GetLoadedExtensions()
                .OfType<IMagicCommand>()
                .FirstOrDefault(m => string.Equals(m.Name, "nuget", StringComparison.OrdinalIgnoreCase));

            if (nugetCommand is not null)
            {
                var packageId = ProviderDiscovery.GetPackageId(resolvedProvider!);
                await nugetCommand.ExecuteAsync(packageId, context).ConfigureAwait(false);
                context.SuppressExecution = true; // restore — nuget command resets it

                if (context.Variables.TryGet<List<string>>("__verso_nuget_assemblies", out var freshPaths)
                    && freshPaths is { Count: > 0 })
                {
                    (factory, providerName, providerError) = ProviderDiscovery.Discover(
                        resolvedCs!, resolvedProvider, freshPaths);
                }
            }
        }

        if (providerError is not null || factory is null)
        {
            await context.WriteOutputAsync(new CellOutput(
                "text/plain", CellText.Error(providerError ?? Strings.Magic_Connect_ProviderUnresolved), IsError: true)).ConfigureAwait(false);
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
                "text/plain", string.Format(Strings.Magic_Connect_OpenFailed, FormatExceptionChain(ex)), IsError: true))
                .ConfigureAwait(false);
            return;
        }

        // Store connection info
        var connInfo = new SqlConnectionInfo(name, resolvedCs!, providerName, connection, commandTimeout);

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

        var redacted = PlaceholderResolver.RedactConnectionString(resolvedCs!);
        var dbName = connection.Database;
        var defaultLabel = isDefault ? Strings.Magic_Connect_DefaultLabel : "";
        var timeoutLabel = commandTimeout switch
        {
            null => "",
            0 => Strings.Magic_Connect_TimeoutNoLimit,
            _ => string.Format(Strings.Magic_Connect_Timeout, commandTimeout),
        };

        // Each line is a whole entry rather than a phrase glued to the one before it, so a
        // language that orders them differently still reads as one report.
        await context.WriteOutputAsync(new CellOutput(
            "text/plain",
            string.Format(Strings.Magic_Connect_Connected,
                name, defaultLabel, providerName ?? Strings.Magic_Connect_ProviderUnknown) +
            (!string.IsNullOrEmpty(dbName) ? string.Format(Strings.Magic_Connect_Database, dbName) : "") +
            string.Format(Strings.Magic_Connect_ConnectionString, redacted) +
            timeoutLabel))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Walks the inner exception chain so the actual root cause is visible to the user.
    /// Many ADO.NET providers wrap the real failure (missing native lib, bad config,
    /// failed type initializer) several layers deep; printing only the outer message
    /// hides the diagnostic that matters.
    /// </summary>
    private static string FormatExceptionChain(Exception ex)
    {
        var parts = new List<string> { $"{ex.GetType().Name}: {ex.Message}" };
        var inner = ex.InnerException;
        var depth = 0;
        while (inner is not null && depth < 5)
        {
            parts.Add($"{inner.GetType().Name}: {inner.Message}");
            inner = inner.InnerException;
            depth++;
        }
        return string.Join(" → ", parts);
    }
}
