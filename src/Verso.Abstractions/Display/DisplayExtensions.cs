namespace Verso.Abstractions;

/// <summary>
/// Extension methods for displaying objects inline during cell execution.
/// </summary>
public static class DisplayExtensions
{
    /// <summary>
    /// Displays the object as rich output in the current cell, using the registered
    /// formatter pipeline. Multiple objects can be displayed within a single cell
    /// without needing to be the final return value.
    /// </summary>
    /// <param name="value">The object to display.</param>
    /// <param name="mimeType">
    /// Optional MIME type hint (e.g. <c>"application/json"</c>, <c>"text/plain"</c>).
    /// When provided, the formatter will prefer this format if the object supports it,
    /// falling back to the default formatter otherwise.
    /// </param>
    public static void Display(this object? value, string? mimeType = null)
    {
        if (value is null) return;

        var handler = DisplayContext.Current
            ?? throw new InvalidOperationException(
                "Display() can only be called during cell execution.");

        handler(value, mimeType).GetAwaiter().GetResult();
    }
}
