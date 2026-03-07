using System.Globalization;
using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Http.Models;

namespace Verso.Http.Parsing;

/// <summary>
/// Resolves <c>{{variable}}</c> placeholders in HTTP request URLs, headers, and bodies.
/// Precedence: file-level @vars → dynamic $vars → variable store.
/// </summary>
internal sealed class HttpVariableResolver
{
    private static readonly Regex PlaceholderPattern = new(
        @"\{\{(.+?)\}\}", RegexOptions.Compiled);

    private readonly Dictionary<string, string> _fileVariables;
    private readonly IVariableStore? _variableStore;
    private readonly Dictionary<string, HttpResponseData> _namedResponses;

    public HttpVariableResolver(
        IReadOnlyList<ParsedVariable> fileVariables,
        IVariableStore? variableStore,
        Dictionary<string, HttpResponseData>? namedResponses = null)
    {
        _variableStore = variableStore;
        _namedResponses = namedResponses ?? new Dictionary<string, HttpResponseData>(StringComparer.OrdinalIgnoreCase);

        // Resolve file-level variables in order (supports self-referencing)
        _fileVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in fileVariables)
        {
            _fileVariables[v.Name] = Resolve(v.Value);
        }
    }

    public string Resolve(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return PlaceholderPattern.Replace(input, match =>
        {
            var expression = match.Groups[1].Value.Trim();

            // 1. File-level @variables
            if (_fileVariables.TryGetValue(expression, out var fileValue))
                return fileValue;

            // 2. Dynamic $variables
            if (expression.StartsWith('$'))
            {
                var resolved = ResolveDynamicVariable(expression);
                if (resolved is not null)
                    return resolved;
            }

            // 3. Named response references
            var responseValue = HttpResponseReference.Resolve(expression, _namedResponses);
            if (responseValue is not null)
                return responseValue;

            // 4. Variable store fallback
            if (_variableStore is not null)
            {
                if (_variableStore.TryGet<object>(expression, out var storeValue) && storeValue is not null)
                    return storeValue.ToString() ?? "";
            }

            // Unresolved — leave as-is
            return match.Value;
        });
    }

    private static string? ResolveDynamicVariable(string expression)
    {
        var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0];

        switch (name)
        {
            case "$guid":
                return Guid.NewGuid().ToString();

            case "$randomInt":
                {
                    int min = 0, max = 1000;
                    if (parts.Length >= 3
                        && int.TryParse(parts[1], out var parsedMin)
                        && int.TryParse(parts[2], out var parsedMax))
                    {
                        min = parsedMin;
                        max = parsedMax;
                    }
                    return Random.Shared.Next(min, max).ToString();
                }

            case "$timestamp":
                {
                    var dt = DateTimeOffset.UtcNow;
                    if (parts.Length >= 3)
                        dt = ApplyOffset(dt, parts[1], parts.Length >= 3 ? parts[2] : "s");
                    return dt.ToUnixTimeSeconds().ToString();
                }

            case "$datetime":
                {
                    var format = parts.Length >= 2 ? NormalizeFormat(parts[1]) : "o";
                    var dt = DateTimeOffset.UtcNow;
                    if (parts.Length >= 4)
                        dt = ApplyOffset(dt, parts[2], parts[3]);
                    return dt.ToString(format, CultureInfo.InvariantCulture);
                }

            case "$localDatetime":
                {
                    var format = parts.Length >= 2 ? NormalizeFormat(parts[1]) : "o";
                    var dt = DateTimeOffset.Now;
                    if (parts.Length >= 4)
                        dt = ApplyOffset(dt, parts[2], parts[3]);
                    return dt.ToString(format, CultureInfo.InvariantCulture);
                }

            case "$processEnv":
                {
                    if (parts.Length >= 2)
                    {
                        var envValue = Environment.GetEnvironmentVariable(parts[1]);
                        return envValue ?? "";
                    }
                    return "";
                }

            default:
                return null;
        }
    }

    private static string NormalizeFormat(string format) => format switch
    {
        "rfc1123" => "R",
        "iso8601" => "o",
        _ => format
    };

    private static DateTimeOffset ApplyOffset(DateTimeOffset dt, string offsetStr, string unit)
    {
        if (!int.TryParse(offsetStr, out var offset))
            return dt;

        return unit switch
        {
            "s" => dt.AddSeconds(offset),
            "m" => dt.AddMinutes(offset),
            "h" => dt.AddHours(offset),
            "d" => dt.AddDays(offset),
            "w" => dt.AddDays(offset * 7),
            "M" => dt.AddMonths(offset),
            "y" => dt.AddYears(offset),
            _ => dt
        };
    }
}
