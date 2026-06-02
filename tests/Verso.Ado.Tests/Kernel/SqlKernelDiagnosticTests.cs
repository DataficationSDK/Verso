using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Abstractions;
using Verso.Ado.Kernel;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Testing.Stubs;

namespace Verso.Ado.Tests.Kernel;

[TestClass]
public sealed class SqlKernelDiagnosticTests
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

    private StubExecutionContext CreateContextWithConnection(
        string providerName = "Microsoft.Data.Sqlite")
    {
        var ctx = new StubExecutionContext();

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // The declared provider name drives dialect resolution. Diagnostics never
        // execute against the connection, so a SQLite connection can stand in while
        // exercising a different dialect's parameter conventions (e.g. Oracle ':').
        var connInfo = new SqlConnectionInfo("testdb", "Data Source=:memory:", providerName, _connection);
        var connections = new Dictionary<string, SqlConnectionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["testdb"] = connInfo
        };

        ctx.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);
        ctx.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, "testdb");

        return ctx;
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_NoVariableStore_ReturnsConnectionError()
    {
        var kernel = new SqlKernel();

        var diagnostics = await kernel.GetDiagnosticsAsync("SELECT 1");

        Assert.IsTrue(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("No database connection")));
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_WithConnection_NoConnectionError()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync("SELECT 1");

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("No database connection")),
            "Should not report missing connection when connection exists.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_NamedConnectionNotFound_ReturnsError()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync("--connection nonexistent\nSELECT 1");

        Assert.IsTrue(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("nonexistent")));
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_UnresolvedParam_ReturnsWarning()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync("SELECT * FROM T WHERE Id = @missingParam");

        Assert.IsTrue(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("@missingParam")));
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_UnresolvedParam_HasCorrectSpan()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var code = "SELECT * FROM T WHERE Id = @missingParam";
        var diagnostics = await kernel.GetDiagnosticsAsync(code);

        var paramDiag = diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("@missingParam"));

        Assert.IsNotNull(paramDiag);
        // @missingParam starts at position 27 in the string
        Assert.AreEqual(0, paramDiag!.StartLine);
        Assert.AreEqual(27, paramDiag.StartColumn);
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_ResolvedParam_NoWarning()
    {
        var ctx = CreateContextWithConnection();
        ctx.Variables.Set("myValue", 42);
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync("SELECT * FROM T WHERE Id = @myValue");

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("@myValue")),
            "Should not warn about resolved parameter.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_MultipleUnresolvedParams_ReturnsMultipleWarnings()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync(
            "SELECT * FROM T WHERE A = @param1 AND B = @param2");

        var paramWarnings = diagnostics.Where(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("Unresolved")).ToList();

        Assert.AreEqual(2, paramWarnings.Count);
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_SqlServerGlobalVariable_NoWarning()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync(
            "SELECT * FROM sys.dm_exec_sessions WHERE session_id != @@SPID");

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("@SPID")),
            "Should not warn about @@SPID — it is a SQL Server global, not a parameter.");
        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("Unresolved")),
            "Should produce no Unresolved-parameter warnings for a query using only globals.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_MixedParamAndGlobal_OnlyWarnsOnParam()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync(
            "SELECT * FROM sessions WHERE id = @userId AND session_id != @@SPID");

        var paramWarnings = diagnostics.Where(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("Unresolved")).ToList();

        Assert.AreEqual(1, paramWarnings.Count, "Only @userId should warn.");
        Assert.IsTrue(paramWarnings[0].Message.Contains("@userId"));
        Assert.IsFalse(paramWarnings.Any(d => d.Message.Contains("@SPID")));
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_GlobalAtStartOfQuery_NoWarning()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync("SELECT @@VERSION");

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("Unresolved")),
            "Should not warn for @@VERSION at start of query.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_DeclaredLocal_NoWarning()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync(
            "DECLARE @x INT; SELECT @x");

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("@x")),
            "Should not warn about a locally DECLAREd variable.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_DeclareMultiVarUserScenario_NoWarning()
    {
        var ctx = CreateContextWithConnection();
        // The kernel variable @waitTime IS in the store and is referenced in the
        // initializer; the locally declared @rows/@waitDelay/@serverName must
        // not produce binding warnings.
        ctx.Variables.Set("waitTime", 30);
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var sql = @"DECLARE @rows INT = 1,
        @waitDelay CHAR(8) = '00:' + RIGHT('0' + CAST(@waitTime AS VARCHAR(2)) + '', 2),
        @serverName SYSNAME = @@SERVERNAME;
SELECT @rows, @waitDelay, @serverName";

        var diagnostics = await kernel.GetDiagnosticsAsync(sql);

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            (d.Message.Contains("@rows") || d.Message.Contains("@waitDelay")
                || d.Message.Contains("@serverName"))),
            "Should not warn about locally DECLAREd variables.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_ParamInLineComment_NoWarning()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync(
            "-- TODO: handle @legacy column\nSELECT 1");

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("@legacy")),
            "Should not warn about @x inside a -- single-line comment.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_ParamInBlockComment_NoWarning()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync(
            "/* uses @legacy column */ SELECT 1");

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("@legacy")),
            "Should not warn about @x inside a /* block comment */.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_ParamInStringLiteral_NoWarning()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync(
            "INSERT INTO Emails VALUES ('alice@example.com')");

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("@example")),
            "Should not warn about @x inside a single-quoted string literal.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_Oracle_UnresolvedColonParam_WarnsWithColonPrefix()
    {
        var ctx = CreateContextWithConnection("Oracle.ManagedDataAccess.Client");
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync(
            "SELECT * FROM T WHERE Id = :userId");

        Assert.IsTrue(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains(":userId")),
            "Oracle unresolved-parameter diagnostics should reference ':userId'.");
        Assert.IsFalse(diagnostics.Any(d => d.Message.Contains("@userId")),
            "Oracle diagnostics must not reference the '@' prefix.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_Oracle_ResolvedColonParam_NoWarning()
    {
        var ctx = CreateContextWithConnection("Oracle.ManagedDataAccess.Client");
        ctx.Variables.Set("userId", 7);
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync(
            "SELECT * FROM T WHERE Id = :userId");

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("Unresolved")),
            "A bound Oracle ':userId' parameter should not warn.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_Oracle_AtParam_NotTreatedAsParameter()
    {
        var ctx = CreateContextWithConnection("Oracle.ManagedDataAccess.Client");
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        var diagnostics = await kernel.GetDiagnosticsAsync(
            "SELECT * FROM T WHERE Id = @userId");

        Assert.IsFalse(diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("Unresolved")),
            "Under Oracle, '@userId' is not a bind parameter and must not warn.");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_DirectiveHeader_SpanRelativeToOriginal()
    {
        var ctx = CreateContextWithConnection();
        var kernel = new SqlKernel();
        await kernel.ExecuteAsync("SELECT 1", ctx);

        // Line 0: directive header, Line 1: the query. The @missing token is at
        // column 27 of line 1 (same column as in the bare-query span test, but
        // shifted down by one line because of the directive header).
        var code = "--connection testdb\nSELECT * FROM T WHERE Id = @missing";
        var diagnostics = await kernel.GetDiagnosticsAsync(code);

        var paramDiag = diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("@missing"));

        Assert.IsNotNull(paramDiag);
        Assert.AreEqual(1, paramDiag!.StartLine,
            "@missing is on line 1 (after the directive header on line 0).");
        Assert.AreEqual(27, paramDiag.StartColumn);
    }
}
