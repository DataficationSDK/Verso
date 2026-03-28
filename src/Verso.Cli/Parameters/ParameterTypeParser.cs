using Verso.Parameters;

namespace Verso.Cli.Parameters;

/// <summary>
/// Converts string values from the CLI into typed CLR objects based on parameter type identifiers.
/// Delegates to <see cref="ParameterValueParser"/> for the actual parsing logic.
/// </summary>
public static class ParameterTypeParser
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
        => ParameterValueParser.TryParse(typeId, value, out result, out error);

    /// <summary>
    /// Returns the set of supported type identifiers.
    /// </summary>
    public static IReadOnlyList<string> SupportedTypes => ParameterValueParser.SupportedTypes;
}
