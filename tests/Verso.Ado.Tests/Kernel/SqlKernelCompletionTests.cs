using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Ado.Kernel;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Testing.Stubs;

namespace Verso.Ado.Tests.Kernel;

[TestClass]
public sealed class SqlKernelCompletionTests
{
    private SqliteConnection? _connection;

    [TestInitialize]
    public void Setup()
    {
        DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", SqliteFactory.Instance);
        // Reset the shared schema cache between tests
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
        cmd.CommandText = "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL)";
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
    public async Task GetCompletionsAsync_NoConnection_ReturnsKeywordsOnly()
    {
        var kernel = new SqlKernel();

        var completions = await kernel.GetCompletionsAsync("SEL", 3);

        Assert.IsTrue(completions.Count > 0);
        Assert.IsTrue(completions.Any(c => c.DisplayText == "SELECT" && c.Kind == "Keyword"));
        Assert.IsFalse(completions.Any(c => c.Kind == "Class"), "Should not have table completions without connection.");
    }

    [TestMethod]
    public async Task GetCompletionsAsync_WithConnection_ReturnsTableCompletions()
    {
        var ctx = CreateContextWithSchema();
        var kernel = new SqlKernel();

        // Execute a query first to populate _lastVariableStore
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var completions = await kernel.GetCompletionsAsync("SELECT * FROM P", 16);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "Products" && c.Kind == "Class"),
            "Should include Products table completion.");
    }

    [TestMethod]
    public async Task GetCompletionsAsync_WithConnection_ReturnsColumnCompletions()
    {
        var ctx = CreateContextWithSchema();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var completions = await kernel.GetCompletionsAsync("SELECT N", 8);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "Name" && c.Kind == "Property"),
            "Should include Name column completion.");
    }

    [TestMethod]
    public async Task GetCompletionsAsync_VariableCompletions()
    {
        var ctx = CreateContextWithSchema();
        ctx.Variables.Set("minPrice", 10.0);
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var completions = await kernel.GetCompletionsAsync("WHERE Price > @min", 18);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "@minPrice" && c.Kind == "Variable"),
            "Should include @minPrice variable completion.");
    }

    [TestMethod]
    public async Task GetCompletionsAsync_FiltersInternalVariables()
    {
        var ctx = CreateContextWithSchema();
        ctx.Variables.Set("__verso_internal", "hidden");
        ctx.Variables.Set("userVar", "visible");
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var completions = await kernel.GetCompletionsAsync("@", 1);

        Assert.IsFalse(completions.Any(c => c.DisplayText.Contains("__verso")),
            "Should not include internal __verso_ variables.");
        Assert.IsTrue(completions.Any(c => c.DisplayText == "@userVar"),
            "Should include user variables.");
    }

    [TestMethod]
    public async Task GetCompletionsAsync_EmptyPrefix_ReturnsAllCompletions()
    {
        var kernel = new SqlKernel();

        var completions = await kernel.GetCompletionsAsync("", 0);

        Assert.IsTrue(completions.Count > 0);
        Assert.IsTrue(completions.Any(c => c.Kind == "Keyword"));
    }

    [TestMethod]
    public async Task GetCompletionsAsync_PartialWord_FiltersResults()
    {
        var kernel = new SqlKernel();

        var completions = await kernel.GetCompletionsAsync("INS", 3);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "INSERT"),
            "INSERT should match 'INS' prefix.");
        Assert.IsFalse(completions.Any(c => c.DisplayText == "SELECT"),
            "SELECT should not match 'INS' prefix.");
        Assert.IsFalse(completions.Any(c => c.DisplayText == "INTERSECT"),
            "INTERSECT starts with 'INT', not 'INS'.");
    }

    [TestMethod]
    public async Task GetCompletionsAsync_SortText_TablesBeforeKeywords()
    {
        var ctx = CreateContextWithSchema();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var completions = await kernel.GetCompletionsAsync("P", 1);

        var tableCompletions = completions.Where(c => c.Kind == "Class").ToList();
        var keywordCompletions = completions.Where(c => c.Kind == "Keyword").ToList();

        if (tableCompletions.Count > 0 && keywordCompletions.Count > 0)
        {
            Assert.IsTrue(
                string.Compare(tableCompletions[0].SortText, keywordCompletions[0].SortText, StringComparison.Ordinal) < 0,
                "Table completions should sort before keyword completions.");
        }
    }
}
