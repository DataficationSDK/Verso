using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Ado.Kernel;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Testing.Stubs;

namespace Verso.Ado.Tests.MagicCommands;

[TestClass]
public sealed class SqlSchemaMagicCommandTests
{
    private SqliteConnection? _connection;

    [TestInitialize]
    public void Setup()
    {
        DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", SqliteFactory.Instance);
        SchemaCache.Instance.InvalidateAll();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection?.Dispose();
        try { DbProviderFactories.UnregisterFactory("Microsoft.Data.Sqlite"); } catch { }
    }

    private StubMagicCommandContext CreateContextWithSchema()
    {
        var ctx = new StubMagicCommandContext();

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL);
            CREATE TABLE Orders (OrderId INTEGER PRIMARY KEY, ProductId INTEGER, Quantity INTEGER);
        ";
        cmd.ExecuteNonQuery();

        var connInfo = new SqlConnectionInfo("testdb", "Data Source=:memory:", "Microsoft.Data.Sqlite", _connection);
        var connections = new Dictionary<string, SqlConnectionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["testdb"] = connInfo
        };

        ctx.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);
        ctx.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, "testdb");

        return ctx;
    }

    [TestMethod]
    public async Task ExecuteAsync_NoArgs_ListsTablesAsHtml()
    {
        var ctx = CreateContextWithSchema();
        var schemaCmd = new SqlSchemaMagicCommand();

        await schemaCmd.ExecuteAsync("", ctx);

        Assert.IsTrue(ctx.SuppressExecution);
        Assert.AreEqual(1, ctx.WrittenOutputs.Count);
        Assert.IsFalse(ctx.WrittenOutputs[0].IsError);
        Assert.AreEqual("text/html", ctx.WrittenOutputs[0].MimeType);
        Assert.IsTrue(ctx.WrittenOutputs[0].Content.Contains("Products"));
        Assert.IsTrue(ctx.WrittenOutputs[0].Content.Contains("Orders"));
    }

    [TestMethod]
    public async Task ExecuteAsync_TableArg_ShowsColumnDetails()
    {
        var ctx = CreateContextWithSchema();
        var schemaCmd = new SqlSchemaMagicCommand();

        await schemaCmd.ExecuteAsync("--table Products", ctx);

        Assert.AreEqual(1, ctx.WrittenOutputs.Count);
        Assert.IsFalse(ctx.WrittenOutputs[0].IsError);
        Assert.IsTrue(ctx.WrittenOutputs[0].Content.Contains("Id"));
        Assert.IsTrue(ctx.WrittenOutputs[0].Content.Contains("Name"));
        Assert.IsTrue(ctx.WrittenOutputs[0].Content.Contains("Price"));
        Assert.IsTrue(ctx.WrittenOutputs[0].Content.Contains("PK"));
    }

    [TestMethod]
    public async Task ExecuteAsync_Refresh_InvalidatesCache()
    {
        var ctx = CreateContextWithSchema();
        var schemaCmd = new SqlSchemaMagicCommand();

        // First call populates cache
        await schemaCmd.ExecuteAsync("", ctx);
        Assert.IsTrue(SchemaCache.Instance.TryGetCached("testdb", out _));

        // Refresh invalidates and re-populates
        ctx = CreateContextWithSchema();
        await schemaCmd.ExecuteAsync("--refresh", ctx);

        Assert.IsFalse(ctx.WrittenOutputs.Any(o => o.IsError));
    }

    [TestMethod]
    public async Task ExecuteAsync_NamedConnection_UsesSpecifiedConnection()
    {
        var ctx = CreateContextWithSchema();
        var schemaCmd = new SqlSchemaMagicCommand();

        await schemaCmd.ExecuteAsync("--connection testdb", ctx);

        Assert.IsFalse(ctx.WrittenOutputs.Any(o => o.IsError));
        Assert.IsTrue(ctx.WrittenOutputs[0].Content.Contains("Products"));
    }

    [TestMethod]
    public async Task ExecuteAsync_NoConnection_ReturnsError()
    {
        var ctx = new StubMagicCommandContext();
        var schemaCmd = new SqlSchemaMagicCommand();

        await schemaCmd.ExecuteAsync("", ctx);

        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError && o.Content.Contains("No database connection")));
    }

    [TestMethod]
    public async Task ExecuteAsync_TableNotFound_ReturnsError()
    {
        var ctx = CreateContextWithSchema();
        var schemaCmd = new SqlSchemaMagicCommand();

        await schemaCmd.ExecuteAsync("--table NonexistentTable", ctx);

        Assert.IsTrue(ctx.WrittenOutputs.Any(o => o.IsError && o.Content.Contains("not found")));
    }
}
