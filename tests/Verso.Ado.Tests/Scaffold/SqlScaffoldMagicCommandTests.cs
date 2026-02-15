using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Abstractions;
using Verso.Ado.Helpers;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Ado.Tests.Helpers;

namespace Verso.Ado.Tests.Scaffold;

[TestClass]
public sealed class SqlScaffoldMagicCommandTests
{
    private SqliteConnection? _connection;

    [TestCleanup]
    public void Cleanup()
    {
        _connection?.Dispose();
    }

    private StubMagicCommandContext CreateContext()
    {
        return new StubMagicCommandContext();
    }

    private void RegisterConnection(StubMagicCommandContext context, string name = "testdb")
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL);";
        cmd.ExecuteNonQuery();

        var connInfo = new SqlConnectionInfo(name, "Data Source=:memory:", "Microsoft.Data.Sqlite", _connection);
        var connections = new Dictionary<string, SqlConnectionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [name] = connInfo
        };
        context.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);
        context.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, name);
    }

    [TestMethod]
    public async Task ExecuteAsync_MissingConnection_ReturnsError()
    {
        var command = new SqlScaffoldMagicCommand();
        var context = CreateContext();

        await command.ExecuteAsync("", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("--connection is required"));
    }

    [TestMethod]
    public async Task ExecuteAsync_UnknownConnection_ReturnsError()
    {
        var command = new SqlScaffoldMagicCommand();
        var context = CreateContext();

        await command.ExecuteAsync("--connection nonexistent", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("not found"));
    }

    [TestMethod]
    public async Task ExecuteAsync_ClosedConnection_ReturnsError()
    {
        var command = new SqlScaffoldMagicCommand();
        var context = CreateContext();

        var closedConn = new SqliteConnection("Data Source=:memory:");
        // Don't open it — leave it closed
        var connInfo = new SqlConnectionInfo("testdb", "Data Source=:memory:", "Microsoft.Data.Sqlite", closedConn);
        var connections = new Dictionary<string, SqlConnectionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["testdb"] = connInfo
        };
        context.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);

        await command.ExecuteAsync("--connection testdb", context);

        Assert.IsTrue(context.SuppressExecution);
        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].IsError);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("not open"));

        closedConn.Dispose();
    }

    [TestMethod]
    public async Task ExecuteAsync_SuppressesExecution_Always()
    {
        var command = new SqlScaffoldMagicCommand();
        var context = CreateContext();

        await command.ExecuteAsync("--connection test", context);

        Assert.IsTrue(context.SuppressExecution);
    }

    [TestMethod]
    public void Parameters_ContainsConnectionAndTables()
    {
        var command = new SqlScaffoldMagicCommand();

        Assert.IsTrue(command.Parameters.Any(p => p.Name == "connection" && p.IsRequired));
        Assert.IsTrue(command.Parameters.Any(p => p.Name == "tables" && !p.IsRequired));
        Assert.IsTrue(command.Parameters.Any(p => p.Name == "schema" && !p.IsRequired));
    }

    [TestMethod]
    public void CommandName_IsSqlScaffold()
    {
        var command = new SqlScaffoldMagicCommand();
        Assert.AreEqual("sql-scaffold", command.Name);
    }

    [TestMethod]
    public void ExtensionId_IsCorrect()
    {
        var command = new SqlScaffoldMagicCommand();
        Assert.AreEqual("verso.ado.magic.sql-scaffold", command.ExtensionId);
    }

    [TestMethod]
    public void TablesArgument_ParsedCorrectly()
    {
        // Verify ArgumentParser handles the comma-separated tables argument
        var args = ArgumentParser.Parse("--connection testdb --tables \"Orders,Products\"");

        Assert.IsTrue(args.ContainsKey("connection"));
        Assert.AreEqual("testdb", args["connection"]);
        Assert.IsTrue(args.ContainsKey("tables"));
        Assert.AreEqual("Orders,Products", args["tables"]);
    }
}
