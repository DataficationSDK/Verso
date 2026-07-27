using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Verso.Ado.Kernel;
using Verso.Ado.MagicCommands;
using Verso.Ado.Models;
using Verso.Testing.Stubs;

namespace Verso.Ado.Tests.Integration;

/// <summary>
/// A database says things that do not arrive with the result set, and until this was wired up
/// Verso discarded all of it: a <c>PRINT</c> produced no output whatsoever, which is the kind of
/// loss a user cannot report because there is no symptom to point at.
/// </summary>
[TestClass]
public sealed class SqlMessageIntegrationTests
{
    [TestMethod]
    public async Task Execute_ServerMessage_ReachesTheCell()
    {
        using var inner = new SqliteConnection("Data Source=:memory:");
        inner.Open();
        Seed(inner);

        using var connection = new MessageRaisingConnection(inner);
        connection.MessagesOnNextCommand.Add("Starting import");
        connection.MessagesOnNextCommand.Add("12 rows processed");

        var context = BuildContext(connection);
        var kernel = new SqlKernel();
        await kernel.InitializeAsync();

        await kernel.ExecuteAsync("SELECT * FROM widgets", context);

        var reported = context.UpdatedOutputs.Where(u => u.OutputBlockId == "sql-messages").ToList();
        Assert.IsTrue(reported.Count > 0, "the server's messages should have reached the cell");

        // Each message revises the same block, so the last value carries the whole log rather than
        // the cell showing one block per line.
        var final = reported[^1].Output;
        StringAssert.Contains(final.Content, "Starting import");
        StringAssert.Contains(final.Content, "12 rows processed");
        Assert.IsFalse(final.IsError, "an informational message is not a failure");
        Assert.IsNull(final.Channel, "a server message is not text a kernel wrote to one of its streams");
    }

    [TestMethod]
    public async Task Execute_Finished_StopsListeningOnTheConnection()
    {
        // Connections outlive the cells that use them, so a subscription left attached would
        // report one cell's messages against the next.
        using var inner = new SqliteConnection("Data Source=:memory:");
        inner.Open();
        Seed(inner);

        using var connection = new MessageRaisingConnection(inner);
        var context = BuildContext(connection);
        var kernel = new SqlKernel();
        await kernel.InitializeAsync();

        await kernel.ExecuteAsync("SELECT * FROM widgets", context);
        var afterExecution = context.UpdatedOutputs.Count;

        connection.Raise("said after the cell finished");

        Assert.AreEqual(afterExecution, context.UpdatedOutputs.Count);
    }

    private static void Seed(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE widgets (id INTEGER, name TEXT); INSERT INTO widgets VALUES (1, 'bolt');";
        command.ExecuteNonQuery();
    }

    private static StubExecutionContext BuildContext(DbConnection connection)
    {
        var context = new StubExecutionContext();
        var connections = new Dictionary<string, SqlConnectionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["testdb"] = new SqlConnectionInfo("testdb", "Data Source=:memory:", "Microsoft.Data.Sqlite", connection)
        };

        context.Variables.Set(SqlConnectMagicCommand.ConnectionsStoreKey, connections);
        context.Variables.Set(SqlConnectMagicCommand.DefaultConnectionStoreKey, "testdb");
        return context;
    }

    /// <summary>
    /// A connection that executes for real against SQLite while also raising the informational
    /// message event a provider would, which SQLite itself has no way to do. Messages are raised as
    /// a command is created, so they land while the cell is running rather than on a timer.
    /// </summary>
    private sealed class MessageRaisingConnection : DbConnection
    {
        private readonly SqliteConnection _inner;

        internal MessageRaisingConnection(SqliteConnection inner) => _inner = inner;

        public delegate void InfoMessageHandler(object sender, ServerMessageEventArgs e);

        // Public because that is how a provider declares it, and how the listener finds it.
        public event InfoMessageHandler? InfoMessage;

        internal List<string> MessagesOnNextCommand { get; } = new();

        internal void Raise(string message) =>
            InfoMessage?.Invoke(this, new ServerMessageEventArgs(message));

        public override string ConnectionString
        {
            get => _inner.ConnectionString;
            set => _inner.ConnectionString = value;
        }

        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;

        public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
        public override void Close() => _inner.Close();
        public override void Open() => _inner.Open();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            _inner.BeginTransaction(isolationLevel);

        protected override DbCommand CreateDbCommand()
        {
            foreach (var message in MessagesOnNextCommand)
                Raise(message);

            MessagesOnNextCommand.Clear();
            return _inner.CreateCommand();
        }
    }

    private sealed class ServerMessageEventArgs : EventArgs
    {
        internal ServerMessageEventArgs(string message) => Message = message;
        public string Message { get; }
    }
}
