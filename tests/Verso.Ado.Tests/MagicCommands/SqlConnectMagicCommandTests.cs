using System.Data.Common;
using Verso.Ado.Helpers;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Testing.Stubs;

namespace Verso.Ado.Tests.MagicCommands;

[TestClass]
public sealed class SqlConnectMagicCommandTests
{
    [TestInitialize]
    public void Setup()
    {
        DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite",
            Microsoft.Data.Sqlite.SqliteFactory.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { DbProviderFactories.UnregisterFactory("Microsoft.Data.Sqlite"); } catch { }
    }

    [TestMethod]
    public async Task ExecuteAsync_ValidConnection_StoresConnectionAndOutputsConfirmation()
    {
        var cmd = new SqlConnectMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync(
            "--name testdb --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite",
            ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.Content.Contains("Connected 'testdb'")));

        var connections = ctx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey);
        Assert.IsNotNull(connections);
        Assert.IsTrue(connections!.ContainsKey("testdb"));
        Assert.IsNotNull(connections["testdb"].Connection);

        // Cleanup
        await connections["testdb"].Connection!.DisposeAsync();
    }

    [TestMethod]
    public async Task ExecuteAsync_MissingName_OutputsError()
    {
        var cmd = new SqlConnectMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("--connection-string \"Data Source=:memory:\"", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError && o.Content.Contains("--name")));
    }

    [TestMethod]
    public async Task ExecuteAsync_MissingConnectionString_OutputsError()
    {
        var cmd = new SqlConnectMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync("--name testdb", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError && o.Content.Contains("--connection-string")));
    }

    [TestMethod]
    public async Task ExecuteAsync_FirstConnection_BecomesDefault()
    {
        var cmd = new SqlConnectMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync(
            "--name testdb --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite",
            ctx);

        var defaultName = ctx.Variables.Get<string>(SqlConnectMagicCommand.DefaultConnectionStoreKey);
        Assert.AreEqual("testdb", defaultName);

        // Cleanup
        var connections = ctx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey)!;
        await connections["testdb"].Connection!.DisposeAsync();
    }

    [TestMethod]
    public async Task ExecuteAsync_DefaultFlag_SetsDefault()
    {
        var cmd = new SqlConnectMagicCommand();
        var ctx = new StubMagicCommandContext();

        // First connection
        await cmd.ExecuteAsync(
            "--name db1 --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite",
            ctx);

        // Second connection with --default
        await cmd.ExecuteAsync(
            "--name db2 --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite --default",
            ctx);

        var defaultName = ctx.Variables.Get<string>(SqlConnectMagicCommand.DefaultConnectionStoreKey);
        Assert.AreEqual("db2", defaultName);

        // Cleanup
        var connections = ctx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey)!;
        foreach (var conn in connections.Values)
            if (conn.Connection is not null)
                await conn.Connection.DisposeAsync();
    }

    [TestMethod]
    public async Task ExecuteAsync_SuppressExecutionAlwaysSet()
    {
        var cmd = new SqlConnectMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync(
            "--name testdb --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite",
            ctx);

        Assert.IsTrue(ctx.SuppressExecution);

        // Cleanup
        var connections = ctx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey)!;
        await connections["testdb"].Connection!.DisposeAsync();
    }

    [TestMethod]
    public async Task ExecuteAsync_UndefinedEnvVar_OutputsError()
    {
        var cmd = new SqlConnectMagicCommand();
        var ctx = new StubMagicCommandContext();

        await cmd.ExecuteAsync(
            "--name testdb --connection-string \"$env:VERSO_NONEXISTENT_VAR_XYZ\" --provider Microsoft.Data.Sqlite",
            ctx);

        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError));
    }
}
