namespace Verso.Abstractions;

/// <summary>
/// Provides ambient access to the active display handler during cell execution.
/// The execution pipeline sets the current handler before each cell runs and
/// clears it afterward. User code reaches this through the <see cref="DisplayExtensions.Display"/>
/// extension method.
/// </summary>
public static class DisplayContext
{
    private static readonly AsyncLocal<Func<object, string?, Task>?> s_current = new();

    /// <summary>
    /// Gets or sets the display handler for the current async execution flow.
    /// The handler accepts the object to display and an optional MIME type hint.
    /// </summary>
    internal static Func<object, string?, Task>? Current
    {
        get => s_current.Value;
        set => s_current.Value = value;
    }

    /// <summary>
    /// Sets the ambient display handler. Returns an <see cref="IDisposable"/> that
    /// restores the previous handler when disposed.
    /// </summary>
    /// <param name="handler">
    /// A callback that accepts the object to display and an optional MIME type hint,
    /// formats it, and writes the output to the cell.
    /// </param>
    public static IDisposable SetHandler(Func<object, string?, Task> handler)
    {
        var previous = s_current.Value;
        s_current.Value = handler ?? throw new ArgumentNullException(nameof(handler));
        return new HandlerScope(previous);
    }

    private sealed class HandlerScope : IDisposable
    {
        private readonly Func<object, string?, Task>? _previous;
        private bool _disposed;

        public HandlerScope(Func<object, string?, Task>? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            s_current.Value = _previous;
        }
    }
}
