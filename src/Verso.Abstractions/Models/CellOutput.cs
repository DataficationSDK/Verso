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
    string? ErrorStackTrace = null);
