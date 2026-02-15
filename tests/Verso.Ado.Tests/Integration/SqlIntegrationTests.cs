using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Data.Sqlite;
using Verso.Ado.Kernel;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Ado.Tests.Helpers;
using Verso.Ado.ToolbarActions;

namespace Verso.Ado.Tests.Integration;

[TestClass]
public sealed class SqlIntegrationTests
{
    [TestInitialize]
    public void Setup()
    {
        DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", SqliteFactory.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { DbProviderFactories.UnregisterFactory("Microsoft.Data.Sqlite"); } catch { }
    }

    [TestMethod]
    public async Task EndToEnd_ConnectCreateInsertSelect_PublishesDataTable()
    {
        var connectCmd = new SqlConnectMagicCommand();
        var magicCtx = new StubMagicCommandContext();

        // Connect
        await connectCmd.ExecuteAsync(
            "--name testdb --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite --default",
            magicCtx);

        Assert.IsFalse(magicCtx.WrittenOutputs.Any(o => o.IsError),
            "Connection failed: " + string.Join("; ", magicCtx.WrittenOutputs.Select(o => o.Content)));

        // Create execution context sharing the same variable store
        var execCtx = new StubExecutionContext();
        // Copy connection state from magic command context
        var connections = magicCtx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey)!;
        execCtx.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);
        execCtx.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey,
            magicCtx.Variables.Get<string>(SqlConnectMagicCommand.DefaultConnectionStoreKey)!);

        var kernel = new SqlKernel();

        // CREATE TABLE
        var createOutputs = await kernel.ExecuteAsync(
            "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL)", execCtx);
        Assert.IsFalse(createOutputs.Any(o => o.IsError),
            "CREATE failed: " + string.Join("; ", createOutputs.Select(o => o.Content)));

        // INSERT rows
        await kernel.ExecuteAsync("INSERT INTO Products VALUES (1, 'Widget', 9.99)", execCtx);
        await kernel.ExecuteAsync("INSERT INTO Products VALUES (2, 'Gadget', 19.99)", execCtx);
        await kernel.ExecuteAsync("INSERT INTO Products VALUES (3, 'Doohickey', 4.99)", execCtx);

        // SELECT
        var selectOutputs = await kernel.ExecuteAsync("SELECT * FROM Products ORDER BY Id", execCtx);

        Assert.IsFalse(selectOutputs.Any(o => o.IsError));
        Assert.IsTrue(selectOutputs.Any(o => o.Content.Contains("Widget")));
        Assert.IsTrue(selectOutputs.Any(o => o.Content.Contains("Gadget")));
        Assert.IsTrue(selectOutputs.Any(o => o.Content.Contains("Doohickey")));

        // Verify DataTable in variable store
        Assert.IsTrue(execCtx.Variables.TryGet<DataTable>("lastSqlResult", out var dt));
        Assert.IsNotNull(dt);
        Assert.AreEqual(3, dt!.Columns.Count); // Id, Name, Price
        Assert.AreEqual(3, dt.Rows.Count);
        Assert.AreEqual("Widget", dt.Rows[0]["Name"]?.ToString());

        // Cleanup
        foreach (var conn in connections.Values)
            if (conn.Connection is not null)
                await conn.Connection.DisposeAsync();
    }

    [TestMethod]
    public async Task EndToEnd_ParameterBinding_FiltersResults()
    {
        var connectCmd = new SqlConnectMagicCommand();
        var magicCtx = new StubMagicCommandContext();

        await connectCmd.ExecuteAsync(
            "--name testdb --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite --default",
            magicCtx);

        var execCtx = new StubExecutionContext();
        var connections = magicCtx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey)!;
        execCtx.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);
        execCtx.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, "testdb");

        var kernel = new SqlKernel();

        await kernel.ExecuteAsync("CREATE TABLE Items (Name TEXT, Price REAL)", execCtx);
        await kernel.ExecuteAsync("INSERT INTO Items VALUES ('Cheap', 5.0)", execCtx);
        await kernel.ExecuteAsync("INSERT INTO Items VALUES ('Expensive', 50.0)", execCtx);

        // Set a C# variable to use as parameter
        execCtx.Variables.Set("minPrice", 10.0);

        var outputs = await kernel.ExecuteAsync(
            "SELECT * FROM Items WHERE Price > @minPrice", execCtx);

        Assert.IsFalse(outputs.Any(o => o.IsError));
        Assert.IsTrue(outputs.Any(o => o.Content.Contains("Expensive")));
        Assert.IsFalse(outputs.Any(o => o.Content.Contains("Cheap")));

        // Verify DataTable
        Assert.IsTrue(execCtx.Variables.TryGet<DataTable>("lastSqlResult", out var dt));
        Assert.AreEqual(1, dt!.Rows.Count);

        foreach (var conn in connections.Values)
            if (conn.Connection is not null)
                await conn.Connection.DisposeAsync();
    }

    [TestMethod]
    public async Task EndToEnd_MultipleConnections_IsolatesByName()
    {
        var connectCmd = new SqlConnectMagicCommand();
        var magicCtx = new StubMagicCommandContext();

        // Connect db1
        await connectCmd.ExecuteAsync(
            "--name db1 --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite --default",
            magicCtx);

        // Connect db2
        await connectCmd.ExecuteAsync(
            "--name db2 --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite",
            magicCtx);

        var execCtx = new StubExecutionContext();
        var connections = magicCtx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey)!;
        execCtx.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);
        execCtx.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, "db1");

        var kernel = new SqlKernel();

        // Create table in db1
        await kernel.ExecuteAsync("--connection db1\nCREATE TABLE T1 (V TEXT)", execCtx);
        await kernel.ExecuteAsync("--connection db1\nINSERT INTO T1 VALUES ('from_db1')", execCtx);

        // Create table in db2
        await kernel.ExecuteAsync("--connection db2\nCREATE TABLE T2 (V TEXT)", execCtx);
        await kernel.ExecuteAsync("--connection db2\nINSERT INTO T2 VALUES ('from_db2')", execCtx);

        // Query db1
        var db1Outputs = await kernel.ExecuteAsync("--connection db1\nSELECT * FROM T1", execCtx);
        Assert.IsTrue(db1Outputs.Any(o => o.Content.Contains("from_db1")));

        // Query db2
        var db2Outputs = await kernel.ExecuteAsync("--connection db2\nSELECT * FROM T2", execCtx);
        Assert.IsTrue(db2Outputs.Any(o => o.Content.Contains("from_db2")));

        foreach (var conn in connections.Values)
            if (conn.Connection is not null)
                await conn.Connection.DisposeAsync();
    }

    [TestMethod]
    public async Task EndToEnd_DisconnectLifecycle_CleansUpProperly()
    {
        var connectCmd = new SqlConnectMagicCommand();
        var disconnectCmd = new SqlDisconnectMagicCommand();
        var magicCtx = new StubMagicCommandContext();

        // Connect
        await connectCmd.ExecuteAsync(
            "--name testdb --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite --default",
            magicCtx);

        // Verify connected
        var connections = magicCtx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey);
        Assert.IsNotNull(connections);
        Assert.IsTrue(connections!.ContainsKey("testdb"));

        // Disconnect
        await disconnectCmd.ExecuteAsync("--name testdb", magicCtx);

        // Verify removed
        connections = magicCtx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey);
        Assert.IsNotNull(connections);
        Assert.IsFalse(connections!.ContainsKey("testdb"));

        // Attempt SQL execution — should get error
        var execCtx = new StubExecutionContext();
        execCtx.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);
        var kernel = new SqlKernel();

        var outputs = await kernel.ExecuteAsync("SELECT 1", execCtx);
        Assert.IsTrue(outputs.Any(o => o.IsError && o.Content.Contains("No database connection")));
    }

    [TestMethod]
    public async Task EndToEnd_SqlKernel_ProducesHtmlOutput()
    {
        var (execCtx, connections) = await SetupConnectionAsync();

        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("CREATE TABLE HtmlTest (Id INTEGER, Name TEXT)", execCtx);
        await kernel.ExecuteAsync("INSERT INTO HtmlTest VALUES (1, 'Row1')", execCtx);

        var outputs = await kernel.ExecuteAsync("SELECT * FROM HtmlTest", execCtx);

        Assert.IsFalse(outputs.Any(o => o.IsError));
        var htmlOutput = outputs.FirstOrDefault(o => o.MimeType == "text/html");
        Assert.IsNotNull(htmlOutput);
        Assert.IsTrue(htmlOutput!.Content.Contains("<table>"));
        Assert.IsTrue(htmlOutput.Content.Contains("Row1"));

        await DisposeConnectionsAsync(connections);
    }

    [TestMethod]
    public async Task EndToEnd_StoresResultSetInVariableStore()
    {
        var (execCtx, connections) = await SetupConnectionAsync();

        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("CREATE TABLE VarTest (Id INTEGER, Val TEXT)", execCtx);
        await kernel.ExecuteAsync("INSERT INTO VarTest VALUES (1, 'test')", execCtx);
        await kernel.ExecuteAsync("SELECT * FROM VarTest", execCtx);

        // Verify DataTable
        Assert.IsTrue(execCtx.Variables.TryGet<DataTable>("lastSqlResult", out var dt));
        Assert.IsNotNull(dt);
        Assert.AreEqual(1, dt!.Rows.Count);

        // Verify SqlResultSet
        Assert.IsTrue(execCtx.Variables.TryGet<SqlResultSet>("lastSqlResult__resultset", out var rs));
        Assert.IsNotNull(rs);
        Assert.AreEqual(1, rs!.Rows.Count);
        Assert.AreEqual(2, rs.Columns.Count);

        await DisposeConnectionsAsync(connections);
    }

    [TestMethod]
    public async Task EndToEnd_ExportCsv_ProducesValidFile()
    {
        var (execCtx, connections) = await SetupConnectionAsync();

        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("CREATE TABLE CsvTest (Name TEXT, Price REAL)", execCtx);
        await kernel.ExecuteAsync("INSERT INTO CsvTest VALUES ('Widget', 9.99)", execCtx);
        await kernel.ExecuteAsync("INSERT INTO CsvTest VALUES ('Gadget', 19.99)", execCtx);
        await kernel.ExecuteAsync("SELECT * FROM CsvTest", execCtx);

        // Set up toolbar action context sharing the same variable store
        var actionCtx = new StubToolbarActionContext
        {
            SelectedCellIds = new[] { execCtx.CellId }
        };

        // Copy variables from execution context to action context
        foreach (var v in execCtx.Variables.GetAll())
            actionCtx.Variables.Set(v.Name, v.Value!);

        var csvAction = new ExportCsvAction();
        Assert.IsTrue(await csvAction.IsEnabledAsync(actionCtx));

        await csvAction.ExecuteAsync(actionCtx);

        Assert.AreEqual(1, actionCtx.DownloadedFiles.Count);
        Assert.AreEqual("text/csv", actionCtx.DownloadedFiles[0].ContentType);

        var csv = Encoding.UTF8.GetString(actionCtx.DownloadedFiles[0].Data);
        Assert.IsTrue(csv.Contains("Name"));
        Assert.IsTrue(csv.Contains("Widget"));
        Assert.IsTrue(csv.Contains("Gadget"));

        await DisposeConnectionsAsync(connections);
    }

    [TestMethod]
    public async Task EndToEnd_NonQuery_ReturnsHtmlRowsAffected()
    {
        var (execCtx, connections) = await SetupConnectionAsync();

        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("CREATE TABLE NqTest (Id INTEGER)", execCtx);

        var outputs = await kernel.ExecuteAsync("INSERT INTO NqTest VALUES (1)", execCtx);

        Assert.IsFalse(outputs.Any(o => o.IsError));
        var htmlOutput = outputs.FirstOrDefault(o => o.MimeType == "text/html");
        Assert.IsNotNull(htmlOutput);
        Assert.IsTrue(htmlOutput!.Content.Contains("row(s) affected"));
        Assert.IsTrue(htmlOutput.Content.Contains("ms"));

        await DisposeConnectionsAsync(connections);
    }

    [TestMethod]
    public async Task EndToEnd_SchemaCompletions_ReturnTableAndColumnNames()
    {
        var (execCtx, connections) = await SetupConnectionAsync();

        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL)", execCtx);
        await kernel.ExecuteAsync("INSERT INTO Products VALUES (1, 'Widget', 9.99)", execCtx);

        // Execute a SELECT to populate _lastVariableStore and schema cache
        await kernel.ExecuteAsync("SELECT * FROM Products", execCtx);

        // Now test completions
        var completions = await kernel.GetCompletionsAsync("SELECT * FROM P", 16);

        Assert.IsTrue(completions.Any(c => c.DisplayText == "Products" && c.Kind == "Class"),
            "Should include Products table in completions.");

        var colCompletions = await kernel.GetCompletionsAsync("SELECT N", 8);
        Assert.IsTrue(colCompletions.Any(c => c.DisplayText == "Name" && c.Kind == "Property"),
            "Should include Name column in completions.");

        await DisposeConnectionsAsync(connections);
    }

    [TestMethod]
    public async Task EndToEnd_JupyterImport_TransformsSqlCells()
    {
        var hook = new Verso.Ado.Import.JupyterSqlImportHook();

        var notebook = new Verso.Abstractions.NotebookModel
        {
            Cells = new List<Verso.Abstractions.CellModel>
            {
                new Verso.Abstractions.CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!connect mssql --kernel-name mydb \"Server=localhost;Database=test\""
                },
                new Verso.Abstractions.CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!mydb\nSELECT * FROM Products"
                },
                new Verso.Abstractions.CellModel
                {
                    Type = "code",
                    Language = "csharp",
                    Source = "#!sql\nSELECT COUNT(*) FROM Orders"
                }
            }
        };

        var result = await hook.PostDeserializeAsync(notebook, "test.ipynb");

        // Verify connect cell was transformed
        Assert.IsTrue(result.Cells.Any(c => c.Source.Contains("#!sql-connect")));

        // Verify #!mydb cell was transformed to SQL with --connection directive
        var mydbCell = result.Cells.FirstOrDefault(c =>
            c.Language == "sql" && c.Source.Contains("--connection mydb"));
        Assert.IsNotNull(mydbCell);

        // Verify #!sql cell was transformed to SQL cell type
        var sqlCell = result.Cells.FirstOrDefault(c =>
            c.Language == "sql" && c.Source.Contains("SELECT COUNT(*)"));
        Assert.IsNotNull(sqlCell);

        // Verify verso.ado is in required extensions
        Assert.IsTrue(result.RequiredExtensions.Contains("verso.ado"));
    }

    // --- Shared test setup helpers ---

    private static async Task<(StubExecutionContext ExecCtx, Dictionary<string, SqlConnectionInfo> Connections)> SetupConnectionAsync()
    {
        var connectCmd = new SqlConnectMagicCommand();
        var magicCtx = new StubMagicCommandContext();

        await connectCmd.ExecuteAsync(
            "--name testdb --connection-string \"Data Source=:memory:\" --provider Microsoft.Data.Sqlite --default",
            magicCtx);

        var execCtx = new StubExecutionContext();
        var connections = magicCtx.Variables.Get<Dictionary<string, SqlConnectionInfo>>(
            SqlConnectMagicCommand.ConnectionsStoreKey)!;
        execCtx.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);
        execCtx.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, "testdb");

        return (execCtx, connections);
    }

    private static async Task DisposeConnectionsAsync(Dictionary<string, SqlConnectionInfo> connections)
    {
        foreach (var conn in connections.Values)
            if (conn.Connection is not null)
                await conn.Connection.DisposeAsync();
    }
}
