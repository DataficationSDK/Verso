namespace Verso.Http.Models;

/// <summary>
/// A single parsed HTTP request from .http file syntax.
/// </summary>
internal sealed class HttpRequestBlock
{
    public string? Name { get; set; }
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Body { get; set; }
    public bool NoRedirect { get; set; }
    public bool NoCookieJar { get; set; }
}
