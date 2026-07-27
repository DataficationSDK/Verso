using System.Collections;
using System.Data.Common;
using System.Reflection;

namespace Verso.Ado.Helpers;

/// <summary>
/// Subscribes to the informational messages a database sends alongside its results, such as
/// SQL Server's <c>PRINT</c> and <c>RAISERROR</c> below the error threshold, or PostgreSQL's
/// <c>RAISE NOTICE</c>.
/// <para>
/// These do not arrive with the result set. They are raised on an event that
/// <see cref="DbConnection"/> does not declare, and each provider names it and shapes its
/// arguments differently, so the subscription is made by reflection for the same reason
/// <see cref="ProviderDiscovery"/> resolves factories that way: the provider assemblies are not
/// referenced at build time and may be loaded from anywhere. A provider with no such event, SQLite
/// among them, simply reports nothing.
/// </para>
/// </summary>
internal static class ProviderMessageListener
{
    /// <summary>
    /// What each provider calls the event. SQL Server, MySQL, and Oracle agree on
    /// <c>InfoMessage</c>; Npgsql calls it <c>Notice</c>.
    /// </summary>
    private static readonly string[] EventNames = { "InfoMessage", "Notice" };

    /// <summary>
    /// Starts reporting the connection's informational messages to <paramref name="onMessage"/>.
    /// Disposing the result stops it, which matters because a connection outlives the cell that
    /// used it and its messages would otherwise be attributed to whatever runs next.
    /// </summary>
    /// <returns>
    /// A subscription to dispose, or null when the provider raises no such event, so the caller
    /// can tell "nothing to listen to" from "listening".
    /// </returns>
    internal static IDisposable? Subscribe(DbConnection connection, Action<string> onMessage)
    {
        if (connection is null || onMessage is null)
            return null;

        var type = connection.GetType();

        foreach (var name in EventNames)
        {
            var declared = type.GetEvent(name, BindingFlags.Public | BindingFlags.Instance);
            if (declared?.EventHandlerType is null || declared.AddMethod is null)
                continue;

            try
            {
                var sink = new Subscription(connection, declared, onMessage);
                sink.Attach();
                return sink;
            }
            catch (Exception ex) when (ex is ArgumentException or MissingMethodException or TargetInvocationException)
            {
                // The event exists but does not have the shape a handler can bind to. Reporting
                // no messages is a far better outcome than failing the cell over one.
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the message text out of a provider's event arguments, whose type is known only at
    /// run time. Returns null when nothing legible can be found, so a shape nobody anticipated
    /// stays silent rather than printing a type name.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so the shapes can be tested without a database.
    /// </remarks>
    internal static string? ReadMessage(object? args)
    {
        if (args is null)
            return null;

        var type = args.GetType();

        // SQL Server and Oracle: a Message string directly on the arguments.
        if (type.GetProperty("Message", BindingFlags.Public | BindingFlags.Instance)?.GetValue(args) is string message
            && !string.IsNullOrWhiteSpace(message))
        {
            return message.TrimEnd('\r', '\n');
        }

        // Npgsql: a Notice object carrying the text.
        if (type.GetProperty("Notice", BindingFlags.Public | BindingFlags.Instance)?.GetValue(args) is object notice)
        {
            var text = notice.GetType()
                .GetProperty("MessageText", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(notice) as string;

            if (!string.IsNullOrWhiteSpace(text))
                return text!.TrimEnd('\r', '\n');
        }

        // MySQL and, for a batch, SQL Server: a collection of errors, each with its own message.
        if (type.GetProperty("Errors", BindingFlags.Public | BindingFlags.Instance)?.GetValue(args) is IEnumerable errors)
        {
            var lines = new List<string>();
            foreach (var error in errors)
            {
                if (error is null)
                    continue;

                var text = error.GetType()
                    .GetProperty("Message", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(error) as string;

                if (!string.IsNullOrWhiteSpace(text))
                    lines.Add(text!.TrimEnd('\r', '\n'));
            }

            if (lines.Count > 0)
                return string.Join(Environment.NewLine, lines);
        }

        return null;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly DbConnection _connection;
        private readonly EventInfo _declared;
        private readonly Action<string> _onMessage;
        private Delegate? _handler;

        internal Subscription(DbConnection connection, EventInfo declared, Action<string> onMessage)
        {
            _connection = connection;
            _declared = declared;
            _onMessage = onMessage;
        }

        internal void Attach()
        {
            // The provider's handler type takes its own arguments class. Binding a method that
            // takes object instead relies on the relaxed delegate binding the runtime allows for
            // reference types, which is what makes one handler serve every provider.
            var method = typeof(Subscription).GetMethod(
                nameof(Handle), BindingFlags.NonPublic | BindingFlags.Instance)!;

            _handler = Delegate.CreateDelegate(_declared.EventHandlerType!, this, method);
            _declared.AddEventHandler(_connection, _handler);
        }

        private void Handle(object? sender, object? args)
        {
            var message = ReadMessage(args);
            if (message is null)
                return;

            // Raised on whichever thread the provider is reading on, and a subscriber that throws
            // there would surface as a connection fault rather than as anything the user could act
            // on. A message is never worth failing a query over.
            try { _onMessage(message); }
            catch { /* nothing legitimate to do with a failure to report a message */ }
        }

        public void Dispose()
        {
            if (_handler is null)
                return;

            try { _declared.RemoveEventHandler(_connection, _handler); }
            catch (Exception ex) when (ex is ArgumentException or TargetInvocationException)
            {
                // A connection disposed before the cell finished takes its subscription with it.
            }

            _handler = null;
        }
    }
}
