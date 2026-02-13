namespace Verso.Abstractions;

/// <summary>
/// Represents rendered content returned by a content renderer or output formatter.
/// </summary>
/// <param name="MimeType">The MIME type of the rendered content (e.g. "text/html", "image/png").</param>
/// <param name="Content">The rendered content payload in the format indicated by <paramref name="MimeType"/>.</param>
public sealed record RenderResult(
    string MimeType,
    string Content);
