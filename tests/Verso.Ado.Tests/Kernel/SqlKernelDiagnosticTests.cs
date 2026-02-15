using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Abstractions;
using Verso.Ado.Kernel;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Ado.Tests.Helpers;

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

    private StubExecutionContext CreateContextWithConnection()
    {
        var ctx = new StubExecutionContext();

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

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
}
