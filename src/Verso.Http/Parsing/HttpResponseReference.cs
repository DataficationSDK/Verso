using System.Text.Json;
using System.Text.RegularExpressions;
using Verso.Http.Models;

namespace Verso.Http.Parsing;

/// <summary>
/// Resolves named response references like <c>{{name.response.body.$.path}}</c>
/// and <c>{{name.response.headers.HeaderName}}</c>.
/// </summary>
internal static class HttpResponseReference
{
    private static readonly Regex ResponseBodyPattern = new(
        @"^(\w+)\.response\.body\.(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ResponseHeaderPattern = new(
        @"^(\w+)\.response\.headers\.(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? Resolve(string expression, Dictionary<string, HttpResponseData> namedResponses)
    {
        if (namedResponses.Count == 0)
            return null;

        // Body reference: name.response.body.$.path
        var bodyMatch = ResponseBodyPattern.Match(expression);
        if (bodyMatch.Success)
        {
            var name = bodyMatch.Groups[1].Value;
            var selector = bodyMatch.Groups[2].Value;

            if (!namedResponses.TryGetValue(name, out var response) || response.Body is null)
                return null;

            return ResolveBodySelector(response.Body, selector);
        }

        // Header reference: name.response.headers.HeaderName
        var headerMatch = ResponseHeaderPattern.Match(expression);
        if (headerMatch.Success)
        {
            var name = headerMatch.Groups[1].Value;
            var headerName = headerMatch.Groups[2].Value;

            if (!namedResponses.TryGetValue(name, out var response))
                return null;

            return response.Headers.TryGetValue(headerName, out var headerValue) ? headerValue : null;
        }

        return null;
    }

    internal static string? ResolveBodySelector(string body, string selector)
    {
        // * = entire body
        if (selector == "*")
            return body;

        // Must start with $
        if (!selector.StartsWith('$'))
            return null;

        // $ alone = entire body
        if (selector == "$")
            return body;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var element = NavigatePath(doc.RootElement, selector.Substring(1));
            if (element is null)
                return null;

            return element.Value.ValueKind switch
            {
                JsonValueKind.String => element.Value.GetString(),
                JsonValueKind.Number => element.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                _ => element.Value.GetRawText()
            };
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement? NavigatePath(JsonElement root, string path)
    {
        if (string.IsNullOrEmpty(path))
            return root;

        var current = root;
        // Split path: .prop, [0], .prop[0].nested
        var segments = ParsePathSegments(path);

        foreach (var segment in segments)
        {
            if (segment.StartsWith('[') && segment.EndsWith(']'))
            {
                // Array index
                var indexStr = segment.Substring(1, segment.Length - 2);
                if (!int.TryParse(indexStr, out var index))
                    return null;

                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                    return null;

                current = current[index];
            }
            else
            {
                // Property name
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var child))
                    return null;

                current = child;
            }
        }

        return current;
    }

    private static List<string> ParsePathSegments(string path)
    {
        var segments = new List<string>();
        int i = 0;

        // Skip leading dot
        if (i < path.Length && path[i] == '.')
            i++;

        while (i < path.Length)
        {
            if (path[i] == '[')
            {
                // Array index segment
                int end = path.IndexOf(']', i);
                if (end < 0) break;
                segments.Add(path.Substring(i, end - i + 1));
                i = end + 1;
                // Skip trailing dot
                if (i < path.Length && path[i] == '.')
                    i++;
            }
            else
            {
                // Property name segment
                int end = i;
                while (end < path.Length && path[end] != '.' && path[end] != '[')
                    end++;
                if (end > i)
                    segments.Add(path.Substring(i, end - i));
                i = end;
                if (i < path.Length && path[i] == '.')
                    i++;
            }
        }

        return segments;
    }
}
