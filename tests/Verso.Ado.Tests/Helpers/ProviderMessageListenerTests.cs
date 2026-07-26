using System.Data;
using System.Data.Common;
using Verso.Ado.Helpers;

namespace Verso.Ado.Tests.Helpers;

/// <summary>
/// The event carrying a database's informational messages is not declared on
/// <see cref="DbConnection"/>, and every provider shapes it differently, so the listener binds to
/// it by reflection. These cover that binding and each argument shape without needing a database:
/// the providers themselves are not referenced here any more than they are in the product.
/// </summary>
[TestClass]
public sealed class ProviderMessageListenerTests
{
    [TestMethod]
    public void Subscribe_ProviderRaisingInfoMessage_ReportsItsText()
    {
        using var connection = new FakeMessageConnection();
        var received = new List<string>();

        using var subscription = ProviderMessageListener.Subscribe(connection, received.Add);

        Assert.IsNotNull(subscription, "an event named InfoMessage should have been found");
        connection.RaiseInfoMessage("Starting import");
        connection.RaiseInfoMessage("Done");

        CollectionAssert.AreEqual(new[] { "Starting import", "Done" }, received);
    }

    [TestMethod]
    public void Subscribe_Disposed_StopsReporting()
    {
        // A connection outlives the cell that used it, so a subscription left attached would
        // report the next cell's messages against this one.
        using var connection = new FakeMessageConnection();
        var received = new List<string>();

        var subscription = ProviderMessageListener.Subscribe(connection, received.Add);
        connection.RaiseInfoMessage("during");
        subscription!.Dispose();
        connection.RaiseInfoMessage("after");

        CollectionAssert.AreEqual(new[] { "during" }, received);
        Assert.IsFalse(connection.HasSubscribers, "disposing must detach the handler");
    }

    [TestMethod]
    public void Subscribe_ProviderWithoutTheEvent_ReportsNothingToListenTo()
    {
        // SQLite raises no such event. That is not a failure, and the caller has to be able to
        // tell it apart from a subscription that is listening.
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");

        var subscription = ProviderMessageListener.Subscribe(connection, _ => { });

        Assert.IsNull(subscription);
    }

    [TestMethod]
    public void ReadMessage_MessageProperty_IsReadDirectly()
    {
        // The shape SQL Server and Oracle use.
        var text = ProviderMessageListener.ReadMessage(new MessageShapedArgs("Changed database context.\r\n"));

        Assert.AreEqual("Changed database context.", text);
    }

    [TestMethod]
    public void ReadMessage_NestedNotice_IsReadThroughIt()
    {
        // The shape Npgsql uses: the text sits on a Notice object rather than on the arguments.
        var text = ProviderMessageListener.ReadMessage(new NoticeShapedArgs(new Notice("table already exists")));

        Assert.AreEqual("table already exists", text);
    }

    [TestMethod]
    public void ReadMessage_ErrorCollection_JoinsEveryMessage()
    {
        var text = ProviderMessageListener.ReadMessage(
            new ErrorsShapedArgs(new[] { new Err("first"), new Err("second") }));

        Assert.AreEqual($"first{Environment.NewLine}second", text);
    }

    [TestMethod]
    public void ReadMessage_ShapeNobodyAnticipated_StaysSilent()
    {
        // Better to report nothing than to print a type name into someone's notebook.
        Assert.IsNull(ProviderMessageListener.ReadMessage(new object()));
        Assert.IsNull(ProviderMessageListener.ReadMessage(null));
        Assert.IsNull(ProviderMessageListener.ReadMessage(new MessageShapedArgs("   ")));
    }

    private sealed class MessageShapedArgs
    {
        public MessageShapedArgs(string message) => Message = message;
        public string Message { get; }
    }

    private sealed class Notice
    {
        public Notice(string messageText) => MessageText = messageText;
        public string MessageText { get; }
    }

    private sealed class NoticeShapedArgs
    {
        public NoticeShapedArgs(Notice notice) => Notice = notice;
        public Notice Notice { get; }
    }

    private sealed class Err
    {
        public Err(string message) => Message = message;
        public string Message { get; }
    }

    private sealed class ErrorsShapedArgs
    {
        public ErrorsShapedArgs(IReadOnlyList<Err> errors) => Errors = errors;
        public IReadOnlyList<Err> Errors { get; }
    }

    /// <summary>
    /// Stands in for a provider connection that raises informational messages through its own
    /// delegate type, which is the arrangement the listener has to bind to without knowing it.
    /// </summary>
    private sealed class FakeMessageConnection : DbConnection
    {
        public delegate void InfoMessageHandler(object sender, FakeInfoMessageEventArgs e);

        public event InfoMessageHandler? InfoMessage;

        public bool HasSubscribers => InfoMessage is not null;

        public void RaiseInfoMessage(string message) =>
            InfoMessage?.Invoke(this, new FakeInfoMessageEventArgs(message));

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class FakeInfoMessageEventArgs : EventArgs
    {
        public FakeInfoMessageEventArgs(string message) => Message = message;
        public string Message { get; }
    }
}
