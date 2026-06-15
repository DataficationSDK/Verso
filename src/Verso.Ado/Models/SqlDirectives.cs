namespace Verso.Ado.Models;

internal sealed record SqlDirectives(
    string? ConnectionName,
    string? VariableName,
    bool NoDisplay,
    int? PageSize,
    int? CommandTimeout = null)
{
    /// <summary>
    /// Parses directive arguments from the first line of SQL code if it matches the
    /// <c>--connection name --name varName --no-display --page-size N --timeout N</c> pattern.
    /// Returns the parsed directives and the remaining SQL code.
    /// </summary>
    internal static (SqlDirectives Directives, string RemainingCode) Parse(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (new SqlDirectives(null, null, false, null), code);

        var lines = code.Split('\n');
        var firstLine = lines[0].TrimStart();

        // Only parse if the first line starts with "--" and contains directive-like tokens
        if (!firstLine.StartsWith("--") || !ContainsDirectiveKey(firstLine))
            return (new SqlDirectives(null, null, false, null), code);

        var args = Verso.Ado.Helpers.ArgumentParser.Parse(firstLine);

        string? connectionName = null;
        string? variableName = null;
        bool noDisplay = false;
        int? pageSize = null;
        int? commandTimeout = null;

        if (args.TryGetValue("connection", out var conn))
            connectionName = conn;

        if (args.TryGetValue("name", out var name))
            variableName = name;

        if (args.ContainsKey("no-display"))
            noDisplay = true;

        if (args.TryGetValue("page-size", out var ps) && int.TryParse(ps, out var psVal))
            pageSize = psVal;

        // Command timeout in seconds; 0 means no limit. A negative value is ignored so
        // the cell falls back to the connection default or the provider default.
        if (args.TryGetValue("timeout", out var to) && int.TryParse(to, out var toVal) && toVal >= 0)
            commandTimeout = toVal;

        var remaining = lines.Length > 1
            ? string.Join('\n', lines.Skip(1))
            : string.Empty;

        return (new SqlDirectives(connectionName, variableName, noDisplay, pageSize, commandTimeout), remaining);
    }

    private static readonly string[] DirectiveKeys =
        { "connection", "name", "no-display", "page-size", "timeout" };

    private static bool ContainsDirectiveKey(string line)
    {
        foreach (var key in DirectiveKeys)
        {
            if (line.Contains("--" + key))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Detects a first line that looks like a directive typed with a space after the
    /// dashes (e.g. <c>-- connection Primary</c>). SQL treats that as an ordinary
    /// comment, so the directive is silently ignored and the cell falls back to the
    /// default connection. Returns a hint message describing the fix, or <c>null</c>
    /// when the first line is not a misused directive.
    /// </summary>
    internal static string? DetectMisusedDirective(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var firstLine = code.Split('\n')[0].TrimStart();

        // Only relevant for a comment that was NOT already parsed as a directive
        // (real directives start with "--connection" etc. and have no space).
        if (!firstLine.StartsWith("--") || ContainsDirectiveKey(firstLine))
            return null;

        var afterDashes = firstLine.Substring(2).TrimStart();
        foreach (var key in DirectiveKeys)
        {
            // The directive name must be the first token, bounded by whitespace or
            // end of line, so prose comments like "-- name: report" do not match.
            if (afterDashes.StartsWith(key, StringComparison.OrdinalIgnoreCase)
                && (afterDashes.Length == key.Length || char.IsWhiteSpace(afterDashes[key.Length])))
            {
                return $"Hint: '--{key}' must have no space after '--'. This line was read as a " +
                       "SQL comment and ignored, so the cell used the default connection.";
            }
        }

        return null;
    }
}
