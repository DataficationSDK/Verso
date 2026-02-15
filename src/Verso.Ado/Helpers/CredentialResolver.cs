using System.Text.RegularExpressions;

namespace Verso.Ado.Helpers;

/// <summary>
/// Expands <c>$env:VAR_NAME</c> and <c>$secret:SecretName</c> tokens in connection strings,
/// and redacts sensitive values for safe display.
/// </summary>
internal static class CredentialResolver
{
    private static readonly Regex EnvPattern = new(@"\$env:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex SecretPattern = new(@"\$secret:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex PasswordPattern = new(
        @"(Password|Pwd)\s*=\s*([^;]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Resolves <c>$env:</c> and <c>$secret:</c> placeholders in a connection string.
    /// Returns <c>(resolvedString, errorMessage)</c>. If errorMessage is non-null, resolution failed.
    /// </summary>
    internal static (string? Resolved, string? Error) ResolveConnectionString(string raw)
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

        // Expand $env: tokens
        string? error = null;
        var resolved = EnvPattern.Replace(raw, match =>
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
    /// Replaces <c>Password=...</c> and <c>Pwd=...</c> values with <c>***</c> for safe display.
    /// </summary>
    internal static string RedactConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        return PasswordPattern.Replace(connectionString, "$1=***");
    }
}
