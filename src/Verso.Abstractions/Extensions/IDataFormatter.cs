namespace Verso.Abstractions;

/// <summary>
/// Formats runtime objects into cell outputs for display. Multiple formatters may be
/// registered; the host selects the best match using <see cref="SupportedTypes"/>,
/// <see cref="Priority"/>, and <see cref="CanFormat"/>.
/// </summary>
public interface IDataFormatter : IExtension
{
    /// <summary>
    /// The set of types this formatter can handle. The host uses this for fast pre-filtering
    /// before calling <see cref="CanFormat"/>.
    /// </summary>
    IReadOnlyList<Type> SupportedTypes { get; }

    /// <summary>
    /// Priority used to resolve conflicts when multiple formatters match the same value.
    /// Higher values take precedence.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether this formatter is a generic fallback rather than a specialized representation.
    /// Language kernels can skip fallback formatters when they have a richer native formatter
    /// for values not claimed by an extension.
    /// </summary>
    bool IsFallback => false;

    /// <summary>
    /// Returns whether this formatter can produce output for the given value in the current context.
    /// When <see cref="IFormatterContext.MimeType"/> specifies a target representation, this
    /// method must return <see langword="false"/> unless <see cref="FormatAsync"/> will emit it.
    /// </summary>
    /// <param name="value">The object to format.</param>
    /// <param name="context">Formatter context providing MIME preferences and display options.</param>
    /// <returns><c>true</c> if this formatter can handle the value; otherwise <c>false</c>.</returns>
    bool CanFormat(object value, IFormatterContext context);

    /// <summary>
    /// Formats the given value into a cell output suitable for display.
    /// The returned MIME type must match <see cref="IFormatterContext.MimeType"/> when the
    /// formatter claimed a MIME-constrained context in <see cref="CanFormat"/>.
    /// </summary>
    /// <param name="value">The object to format.</param>
    /// <param name="context">Formatter context providing MIME preferences and display options.</param>
    /// <returns>A cell output containing the formatted representation of the value.</returns>
    Task<CellOutput> FormatAsync(object value, IFormatterContext context);
}
