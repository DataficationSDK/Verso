using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Ado.Kernel;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Testing.Stubs;

namespace Verso.Ado.Tests.Kernel;

[TestClass]
public sealed class SqlKernelTests
{
    private SqliteConnection? _connection;

    [TestInitialize]
    public void Setup()
    {
        DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", SqliteFactory.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection?.Dispose();
        try { DbProviderFactories.UnregisterFactory("Microsoft.Data.Sqlite"); } catch { }
    }

    private StubExecutionContext CreateContextWithConnection(string name = "testdb")
    {
        var ctx = new StubExecutionContext();

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var connInfo = new SqlConnectionInfo(name, "Data Source=:memory:", "Microsoft.Data.Sqlite", _connection);
        var connections = new Dictionary<string, SqlConnectionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [name] = connInfo
        };

        ctx.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);
        ctx.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, name);

        return ctx;
    }

    [TestMethod]
    public async Task ExecuteAsync_SimpleSelect_ReturnsResults()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        // Create and populate table
        await kernel.ExecuteAsync("CREATE TABLE Test (Id INTEGER, Name TEXT)", ctx);
        await kernel.ExecuteAsync("INSERT INTO Test VALUES (1, 'Alice')", ctx);
        await kernel.ExecuteAsync("INSERT INTO Test VALUES (2, 'Bob')", ctx);

        var outputs = await kernel.ExecuteAsync("SELECT * FROM Test", ctx);

        Assert.IsTrue(outputs.Count > 0);
        Assert.IsFalse(outputs[0].IsError);
        Assert.IsTrue(outputs[0].Content.Contains("Alice"));
        Assert.IsTrue(outputs[0].Content.Contains("Bob"));
    }

    [TestMethod]
    public async Task ExecuteAsync_NoConnection_ReturnsError()
    {
        var ctx = new StubExecutionContext();
        var kernel = new SqlKernel();

        var outputs = await kernel.ExecuteAsync("SELECT 1", ctx);

        Assert.IsTrue(outputs.Any(o => o.IsError && o.Content.Contains("No database connection")));
    }

    [TestMethod]
    public async Task ExecuteAsync_NamedConnectionNotFound_ReturnsError()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        var outputs = await kernel.ExecuteAsync("--connection nonexistent\nSELECT 1", ctx);

        Assert.IsTrue(outputs.Any(o => o.IsError));
    }

    [TestMethod]
    public async Task ExecuteAsync_ParameterBinding_BindsSupportedTypes()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        await kernel.ExecuteAsync("CREATE TABLE Products (Name TEXT, Price REAL)", ctx);
        await kernel.ExecuteAsync("INSERT INTO Products VALUES ('Widget', 9.99)", ctx);
        await kernel.ExecuteAsync("INSERT INTO Products VALUES ('Gadget', 19.99)", ctx);

        ctx.Variables.Set("minPrice", 10.0);

        var outputs = await kernel.ExecuteAsync("SELECT * FROM Products WHERE Price > @minPrice", ctx);

        Assert.IsTrue(outputs.Count > 0);
        Assert.IsFalse(outputs[0].IsError);

        // Verify via the DataTable result (HTML may contain "Widget" in CSS variable names
        // like --vscode-editorWidget-background, so substring checks on HTML are unreliable)
        Assert.IsTrue(ctx.Variables.TryGet<DataTable>("lastSqlResult", out var dt));
        Assert.IsNotNull(dt);
        Assert.AreEqual(1, dt!.Rows.Count);
        Assert.AreEqual("Gadget", dt.Rows[0]["Name"]);
    }

    [TestMethod]
    public async Task ExecuteAsync_AtSignInStringLiteral_NoParameterWarning()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        await kernel.ExecuteAsync("CREATE TABLE Emails (Addr TEXT)", ctx);
        var outputs = await kernel.ExecuteAsync("INSERT INTO Emails VALUES ('alice@example.com')", ctx);

        // Should not produce a "No variable '@example'" warning
        Assert.IsFalse(outputs.Any(o => o.Content.Contains("No variable '@example'")));
    }

    [TestMethod]
    public async Task ExecuteAsync_NoDisplayDirective_SuppressesOutputButPublishesVariable()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        await kernel.ExecuteAsync("CREATE TABLE T1 (X INTEGER)", ctx);
        await kernel.ExecuteAsync("INSERT INTO T1 VALUES (42)", ctx);

        var outputs = await kernel.ExecuteAsync("--no-display --name myResult\nSELECT * FROM T1", ctx);

        // No visible output (only the select - non-query outputs may appear for CREATE/INSERT)
        var selectOutputs = outputs.Where(o => o.Content.Contains("42") || o.Content.Contains("X")).ToList();
        Assert.AreEqual(0, selectOutputs.Count);

        // But variable should be published
        Assert.IsTrue(ctx.Variables.TryGet<DataTable>("myResult", out var dt));
        Assert.IsNotNull(dt);
        Assert.AreEqual(1, dt!.Rows.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_NonQuery_ReturnsRowsAffected()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        await kernel.ExecuteAsync("CREATE TABLE T2 (X INTEGER)", ctx);
        var outputs = await kernel.ExecuteAsync("INSERT INTO T2 VALUES (1)", ctx);

        Assert.IsTrue(outputs.Any(o => o.Content.Contains("row(s) affected")));
    }

    [TestMethod]
    public async Task ExecuteAsync_MultipleStatements_ExecutesAll()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        await kernel.ExecuteAsync("CREATE TABLE T3 (X INTEGER)", ctx);

        var outputs = await kernel.ExecuteAsync(
            "INSERT INTO T3 VALUES (1); INSERT INTO T3 VALUES (2); SELECT COUNT(*) FROM T3", ctx);

        // Should have outputs for inserts and select
        Assert.IsTrue(outputs.Count > 0);
    }

    [TestMethod]
    public async Task ExecuteAsync_ConsecutiveNonQueries_ConsolidatesOutput()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        await kernel.ExecuteAsync("CREATE TABLE T3b (X INTEGER)", ctx);

        var outputs = await kernel.ExecuteAsync(
            "INSERT INTO T3b VALUES (1); INSERT INTO T3b VALUES (2); INSERT INTO T3b VALUES (3)", ctx);

        // Three inserts should produce a single consolidated output, not three separate ones
        var nonQueryOutputs = outputs.Where(o => o.Content.Contains("row(s) affected")).ToList();
        Assert.AreEqual(1, nonQueryOutputs.Count);
        Assert.IsTrue(nonQueryOutputs[0].Content.Contains("3 row(s) affected"));
        Assert.IsTrue(nonQueryOutputs[0].Content.Contains("3 statements"));
    }

    [TestMethod]
    public async Task ExecuteAsync_PublishesDataTable_ToVariableStore()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        await kernel.ExecuteAsync("CREATE TABLE T4 (Id INTEGER, Name TEXT)", ctx);
        await kernel.ExecuteAsync("INSERT INTO T4 VALUES (1, 'Test')", ctx);
        await kernel.ExecuteAsync("SELECT * FROM T4", ctx);

        Assert.IsTrue(ctx.Variables.TryGet<DataTable>("lastSqlResult", out var dt));
        Assert.IsNotNull(dt);
        Assert.AreEqual(2, dt!.Columns.Count);
        Assert.AreEqual(1, dt.Rows.Count);
        Assert.AreEqual("Test", dt.Rows[0]["Name"]?.ToString());
    }

    [TestMethod]
    public async Task ExecuteAsync_CustomVariableName_UsesDirectiveName()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        await kernel.ExecuteAsync("CREATE TABLE T5 (V INTEGER)", ctx);
        await kernel.ExecuteAsync("INSERT INTO T5 VALUES (99)", ctx);

        await kernel.ExecuteAsync("--name customVar\nSELECT * FROM T5", ctx);

        Assert.IsTrue(ctx.Variables.TryGet<DataTable>("customVar", out var dt));
        Assert.IsNotNull(dt);
    }

    [TestMethod]
    public async Task ExecuteAsync_EmptyCode_ReturnsError()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();

        var outputs = await kernel.ExecuteAsync("", ctx);

        Assert.IsTrue(outputs.Any(o => o.IsError));
    }

    [TestMethod]
    public async Task GetCompletionsAsync_ReturnsKeywords()
    {
        var kernel = new SqlKernel();

        var completions = await kernel.GetCompletionsAsync("SELECT", 6);

        Assert.IsTrue(completions.Count > 0, "Should return keyword completions.");
        Assert.IsTrue(completions.Any(c => c.Kind == "Keyword"));
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_WithoutConnection_ReturnsDiagnostics()
    {
        var kernel = new SqlKernel();

        var diagnostics = await kernel.GetDiagnosticsAsync("SELECT 1");

        Assert.IsTrue(diagnostics.Count > 0, "Should return missing-connection diagnostic.");
    }

    [TestMethod]
    public async Task GetHoverInfoAsync_OnKeyword_ReturnsHoverInfo()
    {
        var kernel = new SqlKernel();

        var hover = await kernel.GetHoverInfoAsync("SELECT * FROM T", 3);

        Assert.IsNotNull(hover, "Should return hover info for SQL keyword.");
        Assert.IsTrue(hover!.Content.Contains("SELECT"));
    }
}
