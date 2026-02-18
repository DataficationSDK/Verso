using System.Data.Common;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Testing.Stubs;

namespace Verso.Ado.Tests.MagicCommands;

[TestClass]
public sealed class SqlDisconnectMagicCommandTests
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

    private async Task<StubMagicCommandContext> ConnectAsync(string name = "testdb")
    {
        var connectCmd = new SqlConnectMagicCommand();
        var ctx = new StubMagicCommandContext();
        await connectCmd.ExecuteAsync(
            $"--name {name} --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite --default",
            ctx);
        ctx.WrittenOutputs.Clear();
        return ctx;
    }

    [TestMethod]
    public async Task ExecuteAsync_NamedConnection_Disconnects()
    {
        var ctx = await ConnectAsync();
        var cmd = new SqlDisconnectMagicCommand();

        await cmd.ExecuteAsync("--name testdb", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.Content.Contains("Disconnected 'testdb'")));

        var connections = ctx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey)!;
        Assert.IsFalse(connections.ContainsKey("testdb"));
    }

    [TestMethod]
    public async Task ExecuteAsync_DefaultConnection_DisconnectsDefault()
    {
        var ctx = await ConnectAsync();
        var cmd = new SqlDisconnectMagicCommand();

        // No --name specified, should disconnect default
        await cmd.ExecuteAsync("", ctx);

        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.Content.Contains("Disconnected 'testdb'")));
    }

    [TestMethod]
    public async Task ExecuteAsync_NonexistentConnection_OutputsError()
    {
        var ctx = await ConnectAsync();
        var cmd = new SqlDisconnectMagicCommand();

        await cmd.ExecuteAsync("--name nonexistent", ctx);

        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError && o.Content.Contains("not found")));

        // Cleanup remaining connection
        var connections = ctx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey)!;
        foreach (var conn in connections.Values)
            if (conn.Connection is not null)
                await conn.Connection.DisposeAsync();
    }

    [TestMethod]
    public async Task ExecuteAsync_DisconnectDefault_ClearsDefaultKey()
    {
        var ctx = await ConnectAsync();
        var cmd = new SqlDisconnectMagicCommand();

        await cmd.ExecuteAsync("--name testdb", ctx);

        Assert.IsFalse(ctx.Variables.TryGet<string>(
            SqlConnectMagicCommand.DefaultConnectionStoreKey, out _));
    }

    [TestMethod]
    public async Task ExecuteAsync_DisconnectDefault_WithOtherConnections_SetsNewDefault()
    {
        // Connect two databases
        var connectCmd = new SqlConnectMagicCommand();
        var ctx = new StubMagicCommandContext();

        await connectCmd.ExecuteAsync(
            "--name db1 --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite --default",
            ctx);
        await connectCmd.ExecuteAsync(
            "--name db2 --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite",
            ctx);

        ctx.WrittenOutputs.Clear();

        // Disconnect the default
        var disconnectCmd = new SqlDisconnectMagicCommand();
        await disconnectCmd.ExecuteAsync("--name db1", ctx);

        // db2 should become the new default
        var newDefault = ctx.Variables.Get<string>(SqlConnectMagicCommand.DefaultConnectionStoreKey);
        Assert.AreEqual("db2", newDefault);

        // Cleanup
        var connections = ctx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey)!;
        foreach (var conn in connections.Values)
            if (conn.Connection is not null)
                await conn.Connection.DisposeAsync();
    }

    [TestMethod]
    public async Task ExecuteAsync_NoConnectionsExist_OutputsError()
    {
        var ctx = new StubMagicCommandContext();
        var cmd = new SqlDisconnectMagicCommand();

        await cmd.ExecuteAsync("", ctx);

        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError));
    }
}
