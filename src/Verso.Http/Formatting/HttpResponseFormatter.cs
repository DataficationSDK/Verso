using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Verso.Http.Models;

namespace Verso.Http.Formatting;

/// <summary>
/// Renders <see cref="HttpResponseData"/> as themed HTML.
/// </summary>
internal static class HttpResponseFormatter
{
    private const int MaxBodyDisplayLength = 100 * 1024; // 100 KB

    internal static string FormatResponseHtml(HttpResponseData response)
    {
        var sb = new StringBuilder();

        AppendStyles(sb);

        sb.Append("<div class=\"verso-http-result\">");

        // Status line with badge
        sb.Append("<div class=\"verso-http-status\">");
        sb.Append("<span class=\"verso-http-badge ")
          .Append(GetStatusClass(response.StatusCode))
          .Append("\">")
          .Append(response.StatusCode)
          .Append(' ')
          .Append(WebUtility.HtmlEncode(response.ReasonPhrase ?? ""))
          .Append("</span>");
        sb.Append("<span class=\"verso-http-timing\">")
          .Append(response.ElapsedMs)
          .Append(" ms</span>");
        sb.Append("</div>");

        // Response headers (collapsible)
        if (response.Headers.Count > 0)
        {
            sb.Append("<details class=\"verso-http-headers\">");
            sb.Append("<summary>Response Headers (").Append(response.Headers.Count).Append(")</summary>");
            sb.Append("<table>");
            foreach (var (name, value) in response.Headers)
            {
                sb.Append("<tr><td class=\"verso-http-header-name\">")
                  .Append(WebUtility.HtmlEncode(name))
                  .Append("</td><td>")
                  .Append(WebUtility.HtmlEncode(value))
                  .Append("</td></tr>");
            }
            sb.Append("</table>");
            sb.Append("</details>");
        }

        // Response body
        if (!string.IsNullOrEmpty(response.Body))
        {
            sb.Append("<div class=\"verso-http-body\">");
            var body = response.Body;
            bool truncated = false;

            if (body.Length > MaxBodyDisplayLength)
            {
                body = body.Substring(0, MaxBodyDisplayLength);
                truncated = true;
            }

            var formatted = FormatBody(body, response.ContentType);
            sb.Append("<pre>").Append(WebUtility.HtmlEncode(formatted)).Append("</pre>");

            if (truncated)
            {
                sb.Append("<div class=\"verso-http-truncation\">Response truncated at 100 KB (total: ")
                  .Append((response.Body.Length / 1024).ToString("N0"))
                  .Append(" KB)</div>");
            }

            sb.Append("</div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private static string FormatBody(string body, string? contentType)
    {
        if (contentType is not null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return TryFormatJson(body) ?? body;
        }

        if (contentType is not null && (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("html", StringComparison.OrdinalIgnoreCase)))
        {
            return TryFormatXml(body) ?? body;
        }

        // Try JSON detection
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return TryFormatJson(body) ?? body;
        }

        return body;
    }

    private static string? TryFormatJson(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return null;
        }
    }

    private static string? TryFormatXml(string body)
    {
        try
        {
            var xDoc = XDocument.Parse(body);
            return xDoc.ToString(SaveOptions.None);
        }
        catch
        {
            return null;
        }
    }

    private static string GetStatusClass(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => "verso-http-status-2xx",
        >= 300 and < 400 => "verso-http-status-3xx",
        >= 400 and < 500 => "verso-http-status-4xx",
        >= 500 => "verso-http-status-5xx",
        _ => ""
    };

    internal static void AppendStyles(StringBuilder sb)
    {
        sb.Append("<style>");

        sb.Append(".verso-http-result{");
        sb.Append("--http-bg:var(--vscode-editor-background,var(--verso-cell-output-background,#fff));");
        sb.Append("--http-fg:var(--vscode-editor-foreground,var(--verso-cell-output-foreground,#1e1e1e));");
        sb.Append("--http-border:var(--vscode-editorWidget-border,var(--verso-border-default,#e0e0e0));");
        sb.Append("--http-header-bg:var(--vscode-editorWidget-background,var(--verso-cell-background,#f5f5f5));");
        sb.Append("--http-muted:var(--vscode-descriptionForeground,var(--verso-editor-line-number,#858585));");
        sb.Append("--http-warn-bg:var(--vscode-inputValidation-warningBackground,var(--verso-highlight-background,#fff3cd));");
        sb.Append("--http-warn-fg:var(--vscode-editorWarning-foreground,var(--verso-highlight-foreground,#664d03));");
        sb.Append("--http-warn-border:var(--vscode-inputValidation-warningBorder,var(--verso-status-warning,#ffc107));");
        sb.Append("font-family:var(--verso-code-output-font-family,monospace);font-size:13px;color:var(--http-fg);}");

        // Status line
        sb.Append(".verso-http-status{padding:6px 0;display:flex;align-items:center;gap:8px;}");
        sb.Append(".verso-http-badge{padding:2px 8px;border-radius:4px;font-weight:600;font-size:13px;}");
        sb.Append(".verso-http-status-2xx{background:#d4edda;color:#155724;}");
        sb.Append(".verso-http-status-3xx{background:#cce5ff;color:#004085;}");
        sb.Append(".verso-http-status-4xx{background:#fff3cd;color:#856404;}");
        sb.Append(".verso-http-status-5xx{background:#f8d7da;color:#721c24;}");
        sb.Append(".verso-http-timing{color:var(--http-muted);font-size:12px;}");

        // Headers
        sb.Append(".verso-http-headers{margin:4px 0;}");
        sb.Append(".verso-http-headers summary{cursor:pointer;color:var(--http-muted);font-size:12px;}");
        sb.Append(".verso-http-headers table{border-collapse:collapse;width:auto;margin-top:4px;}");
        sb.Append(".verso-http-headers td{padding:2px 8px;border-bottom:1px solid var(--http-border);font-size:12px;}");
        sb.Append(".verso-http-header-name{font-weight:600;}");

        // Body
        sb.Append(".verso-http-body pre{background:var(--http-header-bg);padding:8px;border-radius:4px;border:1px solid var(--http-border);overflow-x:auto;white-space:pre-wrap;word-break:break-word;margin:4px 0;font-size:13px;}");

        // Truncation warning
        sb.Append(".verso-http-truncation{padding:6px 8px;margin-top:4px;background:var(--http-warn-bg);color:var(--http-warn-fg);border:1px solid var(--http-warn-border);border-radius:4px;font-size:12px;}");

        sb.Append("</style>");
    }
}
