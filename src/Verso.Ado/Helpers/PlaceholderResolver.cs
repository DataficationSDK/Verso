using System.Text.RegularExpressions;
using Verso.Abstractions;

namespace Verso.Ado.Helpers;

/// <summary>
/// Expands <c>$env:VAR_NAME</c>, <c>$var:VarName</c>, and <c>$secret:SecretName</c> tokens
/// in strings, and redacts sensitive values for safe display.
/// </summary>
internal static class PlaceholderResolver
{
    private static readonly Regex EnvPattern = new(@"\$env:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex VarPattern = new(@"\$var:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex SecretPattern = new(@"\$secret:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex PasswordPattern = new(
        @"(Password|Pwd)\s*=\s*([^;]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Resolves <c>$env:</c>, <c>$var:</c>, and <c>$secret:</c> placeholders in a connection string.
    /// Returns <c>(resolvedString, errorMessage)</c>. If errorMessage is non-null, resolution failed.
    /// </summary>
    internal static (string? Resolved, string? Error) ResolveConnectionString(
        string raw, IVariableStore? variables = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, "Connection string is empty.");

        // Check for $secret: tokens first — not supported without user secrets assembly
        var secretMatch = SecretPattern.Match(raw);
        if (secretMatch.Success)
        {
            return (null,
                $"$secret: placeholders are not supported without Microsoft.Extensions.Configuration.UserSecrets. " +
                $"Use #r \"nuget: Microsoft.Extensions.Configuration.UserSecrets\" first, or use $env: with environment variables.");
        }

        // Expand $var: tokens from the notebook variable store
        var (varResolved, varError) = ResolveVariable(raw, variables, "connection string");
        if (varError is not null)
            return (null, varError);

        string? error = null;

        // Expand $env: tokens
        var resolved = EnvPattern.Replace(varResolved!, match =>
        {
            var varName = match.Groups[1].Value;
            var value = Environment.GetEnvironmentVariable(varName);
            if (value is null)
            {
                error = $"Environment variable '{varName}' is not set.";
                return match.Value; // leave unresolved
            }
            return value;
        });

        if (error is not null)
            return (null, error);

        return (resolved, null);
    }

    /// <summary>
    /// Resolves <c>$var:</c> placeholders in any string using the notebook variable store.
    /// Returns <c>(resolvedString, errorMessage)</c>. If errorMessage is non-null, resolution failed.
    /// </summary>
    internal static (string? Resolved, string? Error) ResolveVariable(string raw, IVariableStore? variables, string targetName)
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
    
    /// <summary>
    /// Replaces <c>Password=...</c> and <c>Pwd=...</c> values with <c>***</c> for safe display.
    /// </summary>
    internal static string RedactConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        return PasswordPattern.Replace(connectionString, "$1=***");
    }
}
