namespace Verso.Ado.Models;

internal sealed record SqlDirectives(
    string? ConnectionName,
    string? VariableName,
    bool NoDisplay,
    int? PageSize)
{
    /// <summary>
    /// Parses directive arguments from the first line of SQL code if it matches the
    /// <c>--connection name --name varName --no-display --page-size N</c> pattern.
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

        if (args.TryGetValue("connection", out var conn))
            connectionName = conn;

        if (args.TryGetValue("name", out var name))
            variableName = name;

        if (args.ContainsKey("no-display"))
            noDisplay = true;

        if (args.TryGetValue("page-size", out var ps) && int.TryParse(ps, out var psVal))
            pageSize = psVal;

        var remaining = lines.Length > 1
            ? string.Join('\n', lines.Skip(1))
            : string.Empty;

        return (new SqlDirectives(connectionName, variableName, noDisplay, pageSize), remaining);
    }

    private static bool ContainsDirectiveKey(string line)
    {
        return line.Contains("--connection") ||
               line.Contains("--name") ||
               line.Contains("--no-display") ||
               line.Contains("--page-size");
    }
}
