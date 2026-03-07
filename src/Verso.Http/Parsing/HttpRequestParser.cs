using System.Text.RegularExpressions;
using Verso.Http.Models;

namespace Verso.Http.Parsing;

/// <summary>
/// Parses .http file syntax into variable declarations and request blocks.
/// </summary>
internal static class HttpRequestParser
{
    private static readonly Regex VariablePattern = new(@"^@(\w+)\s*=\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex RequestLinePattern = new(
        @"^(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS|TRACE|CONNECT)\s+(.+?)(?:\s+HTTP/[\d.]+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UrlOnlyPattern = new(
        @"^(https?://.+?)(?:\s+HTTP/[\d.]+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HeaderPattern = new(
        @"^([A-Za-z][\w-]*)\s*:\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex SeparatorPattern = new(
        @"^#{3,}\s*$", RegexOptions.Compiled);
    private static readonly Regex DirectiveNamePattern = new(
        @"^(?:#|//)\s*@name\s+(\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DirectiveNoRedirectPattern = new(
        @"^(?:#|//)\s*@no-redirect\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DirectiveNoCookieJarPattern = new(
        @"^(?:#|//)\s*@no-cookie-jar\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static (List<ParsedVariable> Variables, List<HttpRequestBlock> Requests) Parse(string source)
    {
        var variables = new List<ParsedVariable>();
        var requests = new List<HttpRequestBlock>();

        if (string.IsNullOrWhiteSpace(source))
            return (variables, requests);

        var lines = source.Split('\n');
        int i = 0;

        // Parse file-level variables and collect pre-request directives
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed) || IsComment(trimmed) || SeparatorPattern.IsMatch(trimmed))
            {
                // Check for directives in comments before breaking to request parsing
                if (IsDirective(trimmed))
                    break;

                i++;
                continue;
            }

            var varMatch = VariablePattern.Match(trimmed);
            if (varMatch.Success)
            {
                variables.Add(new ParsedVariable(
                    varMatch.Groups[1].Value,
                    varMatch.Groups[2].Value.Trim(),
                    i));
                i++;
                continue;
            }

            // Not a variable — must be start of request(s)
            break;
        }

        // Parse request blocks
        while (i < lines.Length)
        {
            var request = ParseRequestBlock(lines, ref i);
            if (request is not null)
                requests.Add(request);
        }

        return (variables, requests);
    }

    private static HttpRequestBlock? ParseRequestBlock(string[] lines, ref int i)
    {
        var block = new HttpRequestBlock();
        bool foundRequestLine = false;

        // Skip blank lines and separators, collect directives
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            if (SeparatorPattern.IsMatch(trimmed))
            {
                i++;
                continue;
            }

            if (string.IsNullOrEmpty(trimmed))
            {
                i++;
                continue;
            }

            // Check for directives
            var nameMatch = DirectiveNamePattern.Match(trimmed);
            if (nameMatch.Success)
            {
                block.Name = nameMatch.Groups[1].Value;
                i++;
                continue;
            }

            if (DirectiveNoRedirectPattern.IsMatch(trimmed))
            {
                block.NoRedirect = true;
                i++;
                continue;
            }

            if (DirectiveNoCookieJarPattern.IsMatch(trimmed))
            {
                block.NoCookieJar = true;
                i++;
                continue;
            }

            // Skip other comments
            if (IsComment(trimmed))
            {
                i++;
                continue;
            }

            // Try request line
            var reqMatch = RequestLinePattern.Match(trimmed);
            if (reqMatch.Success)
            {
                block.Method = reqMatch.Groups[1].Value.ToUpperInvariant();
                block.Url = reqMatch.Groups[2].Value.Trim();
                foundRequestLine = true;
                i++;
                break;
            }

            // Try URL-only line (default to GET)
            var urlMatch = UrlOnlyPattern.Match(trimmed);
            if (urlMatch.Success)
            {
                block.Method = "GET";
                block.Url = urlMatch.Groups[1].Value.Trim();
                foundRequestLine = true;
                i++;
                break;
            }

            // Not a recognized request start — skip
            i++;
        }

        if (!foundRequestLine)
            return null;

        // Parse query continuation lines (? or &) and headers
        bool inHeaders = true;
        while (i < lines.Length && inHeaders)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            // Separator ends the request block
            if (SeparatorPattern.IsMatch(trimmed))
                break;

            // Blank line transitions to body
            if (string.IsNullOrEmpty(trimmed))
            {
                i++;
                break;
            }

            // Query continuation
            if (trimmed.StartsWith('?') || trimmed.StartsWith('&'))
            {
                block.Url += trimmed;
                i++;
                continue;
            }

            // Header
            var headerMatch = HeaderPattern.Match(trimmed);
            if (headerMatch.Success)
            {
                block.Headers[headerMatch.Groups[1].Value] = headerMatch.Groups[2].Value.Trim();
                i++;
                continue;
            }

            // Not a header or query continuation — could be body without blank line
            break;
        }

        // Parse body (everything until ### separator or end)
        var bodyLines = new List<string>();
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');

            if (SeparatorPattern.IsMatch(line.Trim()))
                break;

            bodyLines.Add(line);
            i++;
        }

        if (bodyLines.Count > 0)
        {
            var body = string.Join("\n", bodyLines).TrimEnd();
            if (!string.IsNullOrWhiteSpace(body))
                block.Body = body;
        }

        return block;
    }

    private static bool IsComment(string trimmedLine)
    {
        return trimmedLine.StartsWith('#') || trimmedLine.StartsWith("//");
    }

    private static bool IsDirective(string trimmedLine)
    {
        return DirectiveNamePattern.IsMatch(trimmedLine)
            || DirectiveNoRedirectPattern.IsMatch(trimmedLine)
            || DirectiveNoCookieJarPattern.IsMatch(trimmedLine);
    }
}
