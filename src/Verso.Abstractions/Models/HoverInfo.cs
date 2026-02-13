namespace Verso.Abstractions;

/// <summary>
/// Represents hover tooltip information returned by a kernel's language service.
/// </summary>
/// <param name="Content">The tooltip content (e.g. type signature, documentation summary).</param>
/// <param name="MimeType">The MIME type of <paramref name="Content"/>. Defaults to "text/plain".</param>
/// <param name="Range">An optional span identifying the source range the hover applies to, expressed as (StartLine, StartColumn, EndLine, EndColumn) using zero-based indices.</param>
public sealed record HoverInfo(
    string Content,
    string MimeType = "text/plain",
    (int StartLine, int StartColumn, int EndLine, int EndColumn)? Range = null);
