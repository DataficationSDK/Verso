using System.Text;
using System.Text.Json;

namespace Verso.JavaScript.MagicCommands;

/// <summary>A package npm reported on, with the version it resolved to.</summary>
/// <param name="Name">The package name.</param>
/// <param name="Version">The resolved version.</param>
internal readonly record struct NpmPackage(string Name, string Version)
{
    public override string ToString() => Version.Length > 0 ? $"{Name} {Version}" : Name;
}

/// <summary>
/// What an npm install actually did.
///
/// <para>
/// npm narrates a run in prose that counts packages without naming them, then adds its funding
/// and audit footers, and that whole block is saved into the notebook alongside the output the
/// cell was run for. Asked for JSON it reports the same run as data, naming every package and the
/// version it resolved to, which is both shorter to show and the only way to say which version
/// arrived.
/// </para>
/// </summary>
internal sealed class NpmReport
{
    private NpmReport(
        IReadOnlyList<NpmPackage> added,
        IReadOnlyList<NpmPackage> removed,
        IReadOnlyList<string> deprecations,
        string? vulnerabilities,
        string? error,
        bool understood)
    {
        Added = added;
        Removed = removed;
        Deprecations = deprecations;
        Vulnerabilities = vulnerabilities;
        Error = error;
        Understood = understood;
    }

    /// <summary>Packages that were added to the install directory.</summary>
    public IReadOnlyList<NpmPackage> Added { get; }

    /// <summary>Packages that were removed, which is the other half of a replacement.</summary>
    public IReadOnlyList<NpmPackage> Removed { get; }

    /// <summary>
    /// The deprecation notices npm writes while installing. Held back until the detail is asked
    /// for, but kept rather than discarded, which is what happened to them before.
    /// </summary>
    public IReadOnlyList<string> Deprecations { get; }

    /// <summary>
    /// What npm's audit found, when it found anything. Reported whatever the display setting
    /// says: npm reports this on a run that succeeded, and what just landed on the machine
    /// having a known vulnerability is worth hearing about when it happens.
    /// </summary>
    public string? Vulnerabilities { get; }

    /// <summary>npm's own account of a failure, when it gave one.</summary>
    public string? Error { get; }

    /// <summary>
    /// Whether the output was the JSON report it was asked for. An npm too old to produce one
    /// leaves this false, and the caller relays what it did print instead.
    /// </summary>
    public bool Understood { get; }

    /// <summary>
    /// Read a run. <paramref name="standardOutput"/> is npm's JSON report and
    /// <paramref name="standardError"/> is where it puts its warnings, including deprecations,
    /// whether or not JSON was asked for.
    /// </summary>
    public static NpmReport Parse(string standardOutput, string standardError)
    {
        var deprecations = Deprecated(standardError);

        var added = new List<NpmPackage>();
        var removed = new List<NpmPackage>();
        string? vulnerabilities = null;
        string? error = null;

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(standardOutput);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new NpmReport(added, removed, deprecations, null, null, understood: false);
        }

        if (root.ValueKind != JsonValueKind.Object)
            return new NpmReport(added, removed, deprecations, null, null, understood: false);

        ReadPackages(root, "add", added);
        ReadPackages(root, "remove", removed);

        if (root.TryGetProperty("audit", out var audit)
            && audit.TryGetProperty("vulnerabilities", out var counts))
        {
            vulnerabilities = DescribeVulnerabilities(counts);
        }

        if (root.TryGetProperty("error", out var failure))
            error = DescribeError(failure);

        return new NpmReport(added, removed, deprecations, vulnerabilities, error, understood: true);
    }

    private static void ReadPackages(JsonElement root, string property, List<NpmPackage> into)
    {
        if (!root.TryGetProperty(property, out var entries) || entries.ValueKind != JsonValueKind.Array)
            return;

        // npm reports one entry per place a package was written, and a dependency needed at two
        // depths of the tree is written twice. Counting or naming it twice would describe the
        // shape of node_modules rather than what arrived, so identical entries collapse. Two
        // genuinely different versions of one package are kept, because both really are there.
        var seen = new HashSet<NpmPackage>();

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            var name = Text(entry, "name");
            if (name.Length == 0) continue;

            var package = new NpmPackage(name, Text(entry, "version"));
            if (seen.Add(package))
                into.Add(package);
        }
    }

    /// <summary>
    /// npm counts vulnerabilities by severity. Only the severities it actually found are named,
    /// and a run that found none says nothing at all.
    ///
    /// <para>
    /// npm audits the whole install directory rather than only what this run added, so the count
    /// is worded as a fact about the packages that are there. Saying it any other way would read
    /// as an accusation against whatever was just installed, which is usually not where the
    /// advisory came from.
    /// </para>
    /// </summary>
    private static string? DescribeVulnerabilities(JsonElement counts)
    {
        var total = Number(counts, "total");
        if (total <= 0)
            return null;

        var severities = new List<string>();
        foreach (var severity in new[] { "critical", "high", "moderate", "low", "info" })
        {
            var found = Number(counts, severity);
            if (found > 0)
                severities.Add($"{found} {severity}");
        }

        var summary = new StringBuilder("npm audit found ")
            .Append(total == 1 ? "1 vulnerability" : $"{total} vulnerabilities");

        if (severities.Count > 0)
            summary.Append(" (").Append(string.Join(", ", severities)).Append(')');

        return summary.Append(" in the installed packages. Run npm audit for details.").ToString();
    }

    private static string? DescribeError(JsonElement failure)
    {
        if (failure.ValueKind == JsonValueKind.String)
            return failure.GetString();

        if (failure.ValueKind != JsonValueKind.Object)
            return null;

        var summary = Text(failure, "summary");
        var detail = Text(failure, "detail");

        if (summary.Length > 0 && detail.Length > 0)
            return summary + "\n" + detail;

        return summary.Length > 0 ? summary : detail.Length > 0 ? detail : null;
    }

    /// <summary>
    /// The deprecation notices out of npm's warning stream, kept as npm phrased them because the
    /// text says what to use instead.
    /// </summary>
    private static IReadOnlyList<string> Deprecated(string standardError)
    {
        var notices = new List<string>();

        foreach (var raw in (standardError ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Contains("deprecated", StringComparison.OrdinalIgnoreCase)
                && line.StartsWith("npm warn", StringComparison.OrdinalIgnoreCase))
            {
                notices.Add(line);
            }
        }

        return notices;
    }

    // --- reporting ---

    /// <summary>
    /// The lines worth putting in the cell.
    /// </summary>
    /// <param name="requested">
    /// The package names the caller asked for, used to tell what was wanted from what came along
    /// with it.
    /// </param>
    /// <param name="detail">Whether to name every package rather than count the incidental ones.</param>
    public IReadOnlyList<string> Summarize(IReadOnlyList<string> requested, bool detail)
    {
        var lines = new List<string>();
        var wanted = new HashSet<string>(
            (requested ?? Array.Empty<string>()).Where(name => name.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var named = Added.Where(p => wanted.Contains(p.Name)).ToList();
        var rest = Added.Where(p => !wanted.Contains(p.Name)).ToList();

        lines.Add(Headline(named, rest, requested));

        if (Vulnerabilities is { } vulnerabilities)
            lines.Add(vulnerabilities);

        if (!detail)
            return lines;

        if (named.Count > 0 && rest.Count > 0)
            lines.Add("Dependencies: " + Describe(rest) + ".");
        else if (named.Count == 0 && Added.Count > 0)
            lines.Add("Packages: " + Describe(Added) + ".");

        if (Removed.Count > 0)
            lines.Add("Replaced: " + Describe(Removed) + ".");

        lines.AddRange(Deprecations);
        return lines;
    }

    private string Headline(
        IReadOnlyList<NpmPackage> named,
        IReadOnlyList<NpmPackage> rest,
        IReadOnlyList<string>? requested)
    {
        if (Added.Count == 0)
            return "Everything requested is already installed.";

        // Nothing added matches what was asked for, which is what installing from a package file
        // rather than by name looks like from here.
        if (named.Count == 0)
        {
            var packages = (requested ?? Array.Empty<string>()).Count > 0
                ? string.Join(", ", requested!)
                : null;

            return packages is not null
                ? $"Installed {packages} and {Count(rest.Count, "dependency", "dependencies")}."
                : $"Installed {Count(rest.Count, "package")}.";
        }

        var headline = new StringBuilder("Installed ").Append(Describe(named));

        if (rest.Count > 0)
            headline.Append(" and ").Append(Count(rest.Count, "dependency", "dependencies"));

        return headline.Append('.').ToString();
    }

    private static string Describe(IReadOnlyList<NpmPackage> packages)
        => string.Join(", ", packages.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.ToString()));

    private static string Count(int count, string singular, string? plural = null)
        => count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "s"}";

    private static string Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int Number(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var count)
                ? count
                : 0;
}
