using System.Text.RegularExpressions;
using Verso.Abstractions;

namespace Verso.Ado.Helpers;

/// <summary>
/// Resolves <c>$var:VariableName</c> placeholders using the shared variable store.
/// </summary>
internal static class VariablePlaceholderResolver
{
    private static readonly Regex VarPattern = new(@"\$var:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    internal static (string? Resolved, string? Error) Resolve(string raw, IVariableStore? variables, string targetName)
    {
        if (!VarPattern.IsMatch(raw))
            return (raw, null);

        if (variables is null)
            return (null, "$var: placeholders require a variable store but none is available.");

        string? error = null;
        var resolved = VarPattern.Replace(raw, match =>
        {
            var varName = match.Groups[1].Value;
            var allVars = variables.GetAll();
            var descriptor = allVars.FirstOrDefault(v =>
                string.Equals(v.Name, varName, StringComparison.OrdinalIgnoreCase));

            if (descriptor is null)
            {
                error = $"Variable '{varName}' is not defined. Set it in a C# cell before using $var:{varName}.";
                return match.Value;
            }

            var value = descriptor.Value?.ToString();
            if (string.IsNullOrEmpty(value))
            {
                error = $"Variable '{varName}' is null or empty for {targetName}.";
                return match.Value;
            }

            return value;
        });

        return error is null ? (resolved, null) : (null, error);
    }
}
