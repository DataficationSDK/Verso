using System.Text.RegularExpressions;
using Verso.Abstractions;

namespace Verso.Extensions.Utilities;

/// <summary>
/// Shared utility for <c>@variable</c> token substitution used by HTML and Mermaid kernels.
/// Uses the same <c>@(\w+)</c> pattern as SQL parameter binding.
/// </summary>
public static class VariableSubstitution
{
    private static readonly Regex ParamPattern = new(@"@@|@(\w+)", RegexOptions.Compiled);

    /// <summary>
    /// Replaces <c>@variableName</c> tokens with <see cref="object.ToString"/> values from the variable store.
    /// <c>@@</c> is escaped to a literal <c>@</c>. Unresolved variables are left as-is.
    /// </summary>
    /// <param name="source">The source text containing <c>@variable</c> tokens.</param>
    /// <param name="variables">The variable store to resolve values from.</param>
    /// <returns>The source text with resolved variables substituted.</returns>
    public static string Apply(string source, IVariableStore variables)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        return ParamPattern.Replace(source, match =>
        {
            // @@ escape → literal @
            if (match.Value == "@@")
                return "@";

            var varName = match.Groups[1].Value;
            var allVars = variables.GetAll();
            var descriptor = allVars.FirstOrDefault(v =>
                string.Equals(v.Name, varName, StringComparison.OrdinalIgnoreCase));

            if (descriptor is not null && descriptor.Value is not null)
                return descriptor.Value.ToString() ?? match.Value;

            // Unresolved — leave as-is
            return match.Value;
        });
    }

    /// <summary>
    /// Returns the positions of unresolved <c>@variable</c> tokens in the source text.
    /// </summary>
    /// <param name="source">The source text to scan.</param>
    /// <param name="variables">The variable store to check against.</param>
    /// <returns>A list of (VariableName, Offset, Length) tuples for each unresolved variable.</returns>
    public static IReadOnlyList<(string Name, int Offset, int Length)> FindUnresolved(
        string source, IVariableStore variables)
    {
        if (string.IsNullOrEmpty(source))
            return Array.Empty<(string, int, int)>();

        var results = new List<(string, int, int)>();
        var allVars = variables.GetAll();

        foreach (Match match in ParamPattern.Matches(source))
        {
            // Skip @@ escapes
            if (match.Value == "@@")
                continue;

            var varName = match.Groups[1].Value;
            bool resolved = allVars.Any(v =>
                string.Equals(v.Name, varName, StringComparison.OrdinalIgnoreCase));

            if (!resolved)
                results.Add((varName, match.Index, match.Length));
        }

        return results;
    }
}
