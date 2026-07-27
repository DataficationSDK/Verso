namespace Verso.Python.PackageManagement;

/// <summary>
/// Evaluates the version specifier an inline script metadata block can carry, so a notebook
/// declaring a floor can say so when the interpreter in use is below it.
/// <para>
/// Handles the range operators that <c>requires-python</c> is written with in practice, against
/// dotted numeric releases. Equality forms are deliberately not judged: their wildcard and
/// pre-release rules are where a partial reading would produce a wrong warning, and a
/// specifier this cannot read is reported as satisfied. The point is to warn about a mismatch
/// that is certain, not to guess at one.
/// </para>
/// </summary>
internal static class RequiresPython
{
    public static bool IsSatisfied(string? specifier, Version version)
    {
        if (string.IsNullOrWhiteSpace(specifier))
            return true;

        foreach (var clause in specifier!.Split(','))
        {
            var text = clause.Trim();
            if (text.Length == 0)
                continue;

            if (!TrySplit(text, out var op, out var bound))
                return true;

            if (!Compare(op, version, bound))
                return false;
        }

        return true;
    }

    private static bool TrySplit(string clause, out string op, out Version bound)
    {
        op = "";
        bound = new Version(0, 0);

        // Longest operators first, so ">=" is not read as ">".
        foreach (var candidate in new[] { ">=", "<=", "~=", ">", "<" })
        {
            if (!clause.StartsWith(candidate, StringComparison.Ordinal))
                continue;

            op = candidate;
            return TryParseVersion(clause.Substring(candidate.Length), out bound);
        }

        return false;
    }

    /// <summary>
    /// Parse a release segment into a Version. Trailing wildcards and pre-release or local
    /// suffixes are dropped: they change nothing about the major and minor comparison this is
    /// used for, and rejecting them would turn a readable specifier into an unreadable one.
    /// </summary>
    private static bool TryParseVersion(string text, out Version version)
    {
        version = new Version(0, 0);

        var trimmed = text.Trim().TrimEnd('*', '.');
        if (trimmed.Length == 0)
            return false;

        var parts = new List<int>();
        foreach (var segment in trimmed.Split('.'))
        {
            var digits = new string(segment.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0)
                break;

            if (!int.TryParse(digits, out var number))
                return false;

            parts.Add(number);
            if (digits.Length != segment.Length)
                break; // a suffix such as "rc1" ends the release segment
        }

        if (parts.Count == 0)
            return false;

        while (parts.Count < 2)
            parts.Add(0);

        version = parts.Count >= 3
            ? new Version(parts[0], parts[1], parts[2])
            : new Version(parts[0], parts[1]);

        return true;
    }

    private static bool Compare(string op, Version version, Version bound)
    {
        // The interpreter reports major.minor.patch; a bound of "3.9" must not lose to
        // "3.9.0" on an unset field, so both are normalized before comparing.
        var left = Normalize(version);
        var right = Normalize(bound);

        return op switch
        {
            ">=" => left >= right,
            ">" => left > right,
            "<=" => left <= right,
            "<" => left < right,

            // "~=3.9" allows 3.9 and later 3.x, and nothing in 4.
            "~=" => left >= right && left.Major == right.Major,
            _ => true,
        };
    }

    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor < 0 ? 0 : version.Minor,
        version.Build < 0 ? 0 : version.Build);
}
