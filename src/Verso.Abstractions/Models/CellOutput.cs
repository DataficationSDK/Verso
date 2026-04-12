namespace Verso.Abstractions;

/// <summary>
/// Represents the output produced by executing a notebook cell.
/// </summary>
/// <param name="MimeType">The MIME type of <paramref name="Content"/> (e.g. "text/plain", "text/html").</param>
/// <param name="Content">The output content in the format indicated by <paramref name="MimeType"/>.</param>
/// <param name="IsError">Indicates whether this output represents an execution error. Defaults to <see langword="false"/>.</param>
/// <param name="ErrorName">The error type or exception name when <paramref name="IsError"/> is <see langword="true"/>.</param>
/// <param name="ErrorStackTrace">The stack trace associated with the error, if available.</param>
public sealed record CellOutput(
    string MimeType,
    string Content,
    bool IsError = false,
    string? ErrorName = null,
    string? ErrorStackTrace = null)
{
    /// <summary>Creates a <c>text/plain</c> output.</summary>
    public static CellOutput Plain(string content) => new("text/plain", content);

    /// <summary>Creates a <c>text/html</c> output.</summary>
    public static CellOutput Html(string content) => new("text/html", content);

    /// <summary>Creates an <c>image/svg+xml</c> output.</summary>
    public static CellOutput Svg(string content) => new("image/svg+xml", content);

    /// <summary>Creates an <c>image/png</c> output from a Base64-encoded string.</summary>
    public static CellOutput Png(string base64) => new("image/png", base64);

    /// <summary>Creates an <c>image/jpeg</c> output from a Base64-encoded string.</summary>
    public static CellOutput Jpeg(string base64) => new("image/jpeg", base64);

    /// <summary>Creates an <c>image/gif</c> output from a Base64-encoded string.</summary>
    public static CellOutput Gif(string base64) => new("image/gif", base64);

    /// <summary>Creates an <c>image/webp</c> output from a Base64-encoded string.</summary>
    public static CellOutput WebP(string base64) => new("image/webp", base64);

    /// <summary>Creates an <c>image/bmp</c> output from a Base64-encoded string.</summary>
    public static CellOutput Bmp(string base64) => new("image/bmp", base64);

    /// <summary>Creates an <c>application/json</c> output.</summary>
    public static CellOutput Json(string content) => new("application/json", content);

    /// <summary>Creates a <c>text/csv</c> output.</summary>
    public static CellOutput Csv(string content) => new("text/csv", content);

    /// <summary>Creates a <c>text/x-verso-mermaid</c> output.</summary>
    public static CellOutput Mermaid(string content) => new("text/x-verso-mermaid", content);

    /// <summary>Creates an error output with the specified message and optional error name.</summary>
    public static CellOutput Error(string message, string? errorName = null, string? stackTrace = null) =>
        new("text/plain", message, IsError: true, ErrorName: errorName, ErrorStackTrace: stackTrace);
}
