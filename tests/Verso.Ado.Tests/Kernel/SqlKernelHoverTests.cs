using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Ado.Kernel;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Ado.Tests.Helpers;

namespace Verso.Ado.Tests.Kernel;

[TestClass]
public sealed class SqlKernelHoverTests
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

    private StubExecutionContext CreateContextWithSchema()
    {
        var ctx = new StubExecutionContext();

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL)";
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
    public async Task GetHoverInfoAsync_Keyword_ReturnsDescription()
    {
        var kernel = new SqlKernel();

        // "SELECT" is at position 0-5, cursor at 3 (inside "SELECT")
        var hover = await kernel.GetHoverInfoAsync("SELECT * FROM T", 3);

        Assert.IsNotNull(hover);
        Assert.IsTrue(hover!.Content.Contains("SELECT"));
        Assert.AreEqual("text/plain", hover.MimeType);
    }

    [TestMethod]
    public async Task GetHoverInfoAsync_TableName_ReturnsColumnList()
    {
        var ctx = CreateContextWithSchema();
        var kernel = new SqlKernel();

        // Execute to populate _lastVariableStore
        await kernel.ExecuteAsync("SELECT 1", ctx);

        // Call completions first to populate the schema cache
        await kernel.GetCompletionsAsync("SELECT ", 7);

        // Hover over "Products" - starts at position 14
        var hover = await kernel.GetHoverInfoAsync("SELECT * FROM Products", 16);

        Assert.IsNotNull(hover);
        Assert.IsTrue(hover!.Content.Contains("TABLE"), "Should show table type.");
        Assert.IsTrue(hover.Content.Contains("Id"), "Should list Id column.");
        Assert.IsTrue(hover.Content.Contains("Name"), "Should list Name column.");
        Assert.IsTrue(hover.Content.Contains("Price"), "Should list Price column.");
    }

    [TestMethod]
    public async Task GetHoverInfoAsync_ColumnName_ReturnsTypeInfo()
    {
        var ctx = CreateContextWithSchema();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        // Call completions first to populate the schema cache
        await kernel.GetCompletionsAsync("SELECT ", 7);

        // Hover over "Name" at position 7 in "SELECT Name FROM Products"
        var hover = await kernel.GetHoverInfoAsync("SELECT Name FROM Products", 8);

        Assert.IsNotNull(hover);
        Assert.IsTrue(hover!.Content.Contains("Column"), "Should identify as column.");
        Assert.IsTrue(hover.Content.Contains("TEXT"), "Should show data type.");
        Assert.IsTrue(hover.Content.Contains("Products"), "Should show table name.");
    }

    [TestMethod]
    public async Task GetHoverInfoAsync_Variable_ReturnsTypeAndValue()
    {
        var ctx = CreateContextWithSchema();
        ctx.Variables.Set("minPrice", 10.5);
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        // Hover over "@minPrice"
        var hover = await kernel.GetHoverInfoAsync("WHERE Price > @minPrice", 16);

        Assert.IsNotNull(hover);
        Assert.IsTrue(hover!.Content.Contains("Variable"), "Should identify as variable.");
        Assert.IsTrue(hover.Content.Contains("Double"), "Should show type.");
        Assert.IsTrue(hover.Content.Contains("10.5"), "Should show value.");
    }

    [TestMethod]
    public async Task GetHoverInfoAsync_UnknownWord_ReturnsNull()
    {
        var kernel = new SqlKernel();

        var hover = await kernel.GetHoverInfoAsync("xyzzy", 3);

        Assert.IsNull(hover);
    }

    [TestMethod]
    public async Task GetHoverInfoAsync_EmptyCode_ReturnsNull()
    {
        var kernel = new SqlKernel();

        var hover = await kernel.GetHoverInfoAsync("", 0);

        Assert.IsNull(hover);
    }

    [TestMethod]
    public async Task GetHoverInfoAsync_HasRange()
    {
        var kernel = new SqlKernel();

        var hover = await kernel.GetHoverInfoAsync("SELECT * FROM T", 3);

        Assert.IsNotNull(hover);
        Assert.IsNotNull(hover!.Range);
    }
}
