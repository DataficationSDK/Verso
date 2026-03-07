namespace Verso.Http.Models;

/// <summary>
/// Captured HTTP response data.
/// </summary>
internal sealed class HttpResponseData
{
    public int StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Body { get; set; }
    public string? ContentType { get; set; }
    public long ElapsedMs { get; set; }
}
