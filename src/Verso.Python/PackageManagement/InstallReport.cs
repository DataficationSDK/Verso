using System.Text;

namespace Verso.Python.PackageManagement;

/// <summary>A distribution the installer reported on, with the version it settled on.</summary>
/// <param name="Name">The distribution name as the installer spelled it.</param>
/// <param name="Version">The resolved version.</param>
internal readonly record struct InstalledPackage(string Name, string Version)
{
    public override string ToString() => $"{Name} {Version}";
}

/// <summary>
/// What an installer run actually did, read back out of its own output.
///
/// <para>
/// Installing one package pulls in its dependencies, and both pip and uv narrate every step of
/// that: a line to say a distribution is being collected, another for the metadata, another for
/// the download. Twenty-odd packages produce a screen of text that says nothing the person who
/// ran the cell wanted to know, and it is saved into the notebook, so it is still there for
/// whoever opens the file next. What they wanted to know is which packages arrived and at which
/// versions, and both tools do say that, once, in a form worth reading.
/// </para>
/// </summary>
internal sealed class InstallReport
{
    private InstallReport(
        IReadOnlyList<string> output,
        IReadOnlyList<InstalledPackage> installed,
        IReadOnlyList<InstalledPackage> alreadyPresent,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        bool nothingToDo)
    {
        Output = output;
        Installed = installed;
        AlreadyPresent = alreadyPresent;
        Errors = errors;
        Warnings = warnings;
        NothingToDo = nothingToDo;
    }

    /// <summary>Every line the installer wrote, in arrival order, with its own noise removed.</summary>
    public IReadOnlyList<string> Output { get; }

    /// <summary>Distributions that were added to the environment.</summary>
    public IReadOnlyList<InstalledPackage> Installed { get; }

    /// <summary>Distributions the environment already had.</summary>
    public IReadOnlyList<InstalledPackage> AlreadyPresent { get; }

    /// <summary>
    /// Lines the installer marked as errors. Kept whatever the display setting says, because pip
    /// reports a dependency conflict this way and still exits successfully, and an environment
    /// that no longer agrees with itself is worth hearing about the moment it happens.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Lines the installer marked as warnings, shown only when the detail is asked for.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Whether the installer said outright that there was nothing to do.</summary>
    public bool NothingToDo { get; }

    /// <summary>
    /// Whether anything in the output was understood. When nothing was, the caller falls back to
    /// the installer's own text rather than inventing a summary of a run it cannot describe.
    /// </summary>
    public bool Recognized => Installed.Count > 0 || AlreadyPresent.Count > 0 || NothingToDo;

    /// <summary>
    /// Read an installer's output. Both pip and uv are understood, because either can run: uv is
    /// preferred when it is on the path and pip is the fallback, and which one ran is not
    /// something the person reading the cell should have to think about.
    /// </summary>
    public static InstallReport Parse(IReadOnlyList<string> lines)
    {
        var output = new List<string>();
        var installed = new List<InstalledPackage>();
        var present = new List<InstalledPackage>();
        var errors = new List<string>();
        var warnings = new List<string>();
        var nothingToDo = false;

        foreach (var raw in lines ?? Array.Empty<string>())
        {
            var line = raw?.TrimEnd() ?? string.Empty;
            if (line.Length == 0)
                continue;

            // pip's upgrade nag is about pip, not about the notebook, and it is printed on every
            // run once the interpreter's pip falls behind.
            if (line.TrimStart().StartsWith("[notice]", StringComparison.Ordinal))
                continue;

            output.Add(line);

            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("ERROR:", StringComparison.Ordinal))
            {
                errors.Add(trimmed);
                continue;
            }

            if (trimmed.StartsWith("WARNING:", StringComparison.Ordinal))
            {
                warnings.Add(trimmed);
                continue;
            }

            if (TryReadPipInstalled(trimmed, installed))
                continue;

            if (TryReadPipSatisfied(trimmed, present))
                continue;

            if (TryReadUvInstalled(trimmed, installed))
                continue;

            if (IsUvNothingToDo(trimmed))
                nothingToDo = true;
        }

        return new InstallReport(output, installed, present, errors, warnings, nothingToDo);
    }

    // --- pip ---

    /// <summary>
    /// <c>Successfully installed certifi-2026.7.22 idna-3.18</c>, which pip prints once at the end
    /// with every distribution it added and the version it chose.
    /// </summary>
    private static bool TryReadPipInstalled(string line, List<InstalledPackage> into)
    {
        const string prefix = "Successfully installed ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        foreach (var token in line[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TrySplitVersion(token, out var name, out var version))
                into.Add(new InstalledPackage(name, version));
        }

        return true;
    }

    /// <summary>
    /// <c>Requirement already satisfied: idna&lt;4,&gt;=2.5 in /path (from requests) (3.18)</c>. The
    /// name carries the constraint it was asked for and the version in use is the last
    /// parenthesized group, whether or not the line also says what asked for it.
    /// </summary>
    private static bool TryReadPipSatisfied(string line, List<InstalledPackage> into)
    {
        const string prefix = "Requirement already satisfied: ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var body = line[prefix.Length..];

        var location = body.IndexOf(" in ", StringComparison.Ordinal);
        var specifier = location >= 0 ? body[..location] : body;

        var version = string.Empty;
        if (body.EndsWith(')') && body.LastIndexOf('(') is var open && open >= 0)
            version = body[(open + 1)..^1];

        into.Add(new InstalledPackage(NameOf(specifier), version));
        return true;
    }

    // --- uv ---

    /// <summary>
    /// <c> + certifi==2026.7.22</c>, one line per distribution added. A <c>-</c> line is the
    /// other half of a replacement and is left alone, because the version that matters is the one
    /// the environment ended up with.
    /// </summary>
    private static bool TryReadUvInstalled(string line, List<InstalledPackage> into)
    {
        if (!line.StartsWith("+ ", StringComparison.Ordinal))
            return false;

        var separator = line.IndexOf("==", StringComparison.Ordinal);
        if (separator < 0)
            return false;

        var name = line[2..separator].Trim();
        var version = line[(separator + 2)..].Trim();
        if (name.Length == 0)
            return false;

        into.Add(new InstalledPackage(name, version));
        return true;
    }

    /// <summary>
    /// uv says <c>Checked 1 package</c> or <c>Audited 5 packages</c> when the environment already
    /// satisfies the request, and names none of them.
    /// </summary>
    private static bool IsUvNothingToDo(string line)
        => line.StartsWith("Checked ", StringComparison.Ordinal)
            || line.StartsWith("Audited ", StringComparison.Ordinal);

    // --- reporting ---

    /// <summary>
    /// The lines worth putting in the cell.
    /// </summary>
    /// <param name="requested">
    /// The specifiers the caller asked for, used to tell what was wanted from what came along with
    /// it. A dependency count reads as incidental; twenty-four package names do not.
    /// </param>
    /// <param name="detail">Whether to name every distribution rather than count them.</param>
    public IReadOnlyList<string> Summarize(IReadOnlyList<string> requested, bool detail)
    {
        var lines = new List<string>();
        var wanted = Wanted(requested);

        var named = Installed.Where(p => wanted.Contains(Normalize(p.Name))).ToList();
        var rest = Installed.Where(p => !wanted.Contains(Normalize(p.Name))).ToList();

        if (Installed.Count > 0)
            lines.Add(InstalledHeadline(named, rest));
        else if (Recognized)
            lines.Add(PresentHeadline(AlreadyPresent.Where(p => wanted.Contains(Normalize(p.Name))).ToList()));
        else
            lines.Add(UnreadHeadline(requested));

        // Kept ahead of the detail because an install that quietly broke something else in the
        // environment matters more than what it put there.
        lines.AddRange(Errors);

        if (!detail)
            return lines;

        if (named.Count > 0 && rest.Count > 0)
            lines.Add("Dependencies: " + Describe(rest) + ".");
        else if (named.Count == 0 && Installed.Count > 0)
            lines.Add("Packages: " + Describe(Installed) + ".");

        if (AlreadyPresent.Count > 0)
            lines.Add("Already present: " + Describe(AlreadyPresent) + ".");

        lines.AddRange(Warnings);
        return lines;
    }

    private static string InstalledHeadline(
        IReadOnlyList<InstalledPackage> named, IReadOnlyList<InstalledPackage> rest)
    {
        // Nothing installed matches what was asked for, which is what a requirements file or an
        // installer option that resolves to something else looks like from here.
        if (named.Count == 0)
            return $"Installed {Count(rest.Count, "package")}.";

        var headline = new StringBuilder("Installed ").Append(Describe(named));

        if (rest.Count > 0)
            headline.Append(" and ").Append(Count(rest.Count, "dependency", "dependencies"));

        return headline.Append('.').ToString();
    }

    private static string PresentHeadline(IReadOnlyList<InstalledPackage> named)
    {
        if (named.Count == 0)
            return "Everything requested is already installed.";

        return named.Count == 1
            ? $"{Describe(named)} is already installed."
            : $"{Describe(named)} are already installed.";
    }

    /// <summary>
    /// What to say when the run succeeded but its output said nothing this understands, which is
    /// an installer or a version that has changed how it reports. Names what was asked for rather
    /// than claiming a version nothing confirmed.
    /// </summary>
    private static string UnreadHeadline(IReadOnlyList<string>? requested)
    {
        var packages = (requested ?? Array.Empty<string>())
            .Where(specifier => !string.IsNullOrWhiteSpace(specifier)
                && !specifier.TrimStart().StartsWith('-'))
            .ToList();

        return packages.Count > 0
            ? $"Installed {string.Join(", ", packages)}."
            : "Install completed.";
    }

    /// <summary>
    /// The distribution names the caller asked for, normalized for comparison. An installer
    /// option is skipped: it is not a package, so nothing it produces can match it.
    /// </summary>
    private static HashSet<string> Wanted(IReadOnlyList<string>? requested)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var specifier in requested ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(specifier) || specifier.TrimStart().StartsWith('-'))
                continue;

            var name = Normalize(NameOf(specifier));
            if (name.Length > 0)
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// The distribution name at the head of a requirement, dropping any version constraint,
    /// extras or environment marker that follows it.
    /// </summary>
    private static string NameOf(string specifier)
    {
        var end = specifier.AsSpan().IndexOfAny("<>=!~[;(, ");
        return (end < 0 ? specifier : specifier[..end]).Trim();
    }

    /// <summary>
    /// Distribution names compare case-insensitively with the runs of <c>-</c>, <c>_</c> and
    /// <c>.</c> between their parts treated as equivalent, which is why a request for
    /// <c>charset-normalizer</c> matches a report of <c>charset_normalizer</c>.
    /// </summary>
    private static string Normalize(string name)
    {
        var result = new StringBuilder(name.Length);
        var separated = false;

        foreach (var character in name.Trim())
        {
            if (character is '-' or '_' or '.')
            {
                separated = result.Length > 0;
                continue;
            }

            if (separated)
            {
                result.Append('-');
                separated = false;
            }

            result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }

    /// <summary>
    /// Splits a <c>name-version</c> token at the last hyphen that begins the version, so a
    /// distribution whose own name contains one is not cut in the wrong place.
    /// </summary>
    private static bool TrySplitVersion(string token, out string name, out string version)
    {
        for (var index = token.Length - 2; index > 0; index--)
        {
            if (token[index] != '-' || !char.IsDigit(token[index + 1]))
                continue;

            name = token[..index];
            version = token[(index + 1)..];
            return true;
        }

        name = token;
        version = string.Empty;
        return false;
    }

    private static string Describe(IReadOnlyList<InstalledPackage> packages)
        => string.Join(", ", packages.Select(p =>
            p.Version.Length > 0 ? $"{p.Name} {p.Version}" : p.Name));

    private static string Count(int count, string singular, string? plural = null)
        => count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "s"}";
}
