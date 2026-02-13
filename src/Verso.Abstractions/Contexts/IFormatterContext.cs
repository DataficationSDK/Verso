namespace Verso.Abstractions;

/// <summary>
/// Context provided to data formatters when converting objects to display representations. Extends <see cref="IVersoContext"/> with formatting constraints.
/// </summary>
public interface IFormatterContext : IVersoContext
{
    /// <summary>
    /// Gets the target MIME type for the formatted output (e.g., "text/html", "text/plain").
    /// </summary>
    string MimeType { get; }

    /// <summary>
    /// Gets the maximum width available for the formatted output, in device-independent units.
    /// </summary>
    double MaxWidth { get; }

    /// <summary>
    /// Gets the maximum height available for the formatted output, in device-independent units.
    /// </summary>
    double MaxHeight { get; }
}
