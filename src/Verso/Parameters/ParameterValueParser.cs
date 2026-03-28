using System.Globalization;

namespace Verso.Parameters;

/// <summary>
/// Parses string values into typed CLR objects based on parameter type identifiers.
/// Shared between the engine (interaction handler) and the CLI (ParameterTypeParser).
/// </summary>
public static class ParameterValueParser
{
    /// <summary>
    /// Attempts to parse a string value into a CLR object according to the declared parameter type.
    /// </summary>
    /// <param name="typeId">The type identifier: string, int, float, bool, date, or datetime.</param>
    /// <param name="value">The string value to parse.</param>
    /// <param name="result">The parsed CLR object on success.</param>
    /// <param name="error">A descriptive error message on failure.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise <c>false</c>.</returns>
    public static bool TryParse(string typeId, string value, out object? result, out string? error)
    {
        result = null;
        error = null;

        switch (typeId)
        {
            case "string":
                result = value;
                return true;

            case "int":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
                {
                    result = longVal;
                    return true;
                }
                error = $"Expected an integer value, got '{value}'.";
                return false;

            case "float":
                if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleVal))
                {
                    result = doubleVal;
                    return true;
                }
                error = $"Expected a numeric value, got '{value}'.";
                return false;

            case "bool":
                switch (value.ToLowerInvariant())
                {
                    case "true" or "yes" or "1":
                        result = true;
                        return true;
                    case "false" or "no" or "0":
                        result = false;
                        return true;
                    default:
                        error = $"Expected true/false, yes/no, or 1/0, got '{value}'.";
                        return false;
                }

            case "date":
                if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateVal))
                {
                    result = dateVal;
                    return true;
                }
                error = $"Expected a date in yyyy-MM-dd format, got '{value}'.";
                return false;

            case "datetime":
                if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtoVal))
                {
                    // Default to UTC if no offset was specified
                    if (!value.Contains('+') && !value.Contains('Z') && !value.EndsWith('-'))
                    {
                        dtoVal = new DateTimeOffset(dtoVal.DateTime, TimeSpan.Zero);
                    }
                    result = dtoVal;
                    return true;
                }
                error = $"Expected an ISO 8601 datetime value, got '{value}'.";
                return false;

            default:
                error = $"Unknown parameter type '{typeId}'.";
                return false;
        }
    }

    /// <summary>
    /// Returns the set of supported type identifiers.
    /// </summary>
    public static IReadOnlyList<string> SupportedTypes { get; } =
        new[] { "string", "int", "float", "bool", "date", "datetime" };
}
