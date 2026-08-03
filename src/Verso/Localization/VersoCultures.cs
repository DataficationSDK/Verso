using System.Globalization;

namespace Verso.Localization;

/// <summary>
/// The languages Verso ships an interface in, and the rules for picking one.
/// </summary>
/// <remarks>
/// Every host answers the same question differently: the CLI has a <c>--language</c> option, the
/// web server has an <c>Accept-Language</c> header, the editor extension has a setting. They all
/// end up here so that the answer is the same wherever it was asked, and so that adding a
/// language is one entry rather than a search through four projects.
/// </remarks>
public static class VersoCultures
{
    /// <summary>The language used when nothing else resolves, and the one every string is written in.</summary>
    public const string Default = "en";

    /// <summary>
    /// Environment variable read when no language was requested explicitly. It exists for
    /// containers and CI, where passing an option to every invocation is awkward.
    /// </summary>
    public const string EnvironmentVariable = "VERSO_LANGUAGE";

    /// <summary>
    /// A generated stand-in for a real translation. Every string is accented and bracketed, so
    /// running the interface in it makes anything still in plain English obvious at a glance,
    /// and the padding shows where a longer translation would be clipped. It is deliberately
    /// absent from <see cref="Supported"/> so it never appears in a language picker; ask for it
    /// by name.
    /// </summary>
    public const string Pseudo = "qps-Ploc";

    /// <summary>
    /// The command-line option every host accepts to name a language.
    /// </summary>
    public const string Option = "--language";

    /// <summary>
    /// The languages offered in the interface, in the order a picker should list them.
    /// </summary>
    public static IReadOnlyList<string> Supported { get; } = new[]
    {
        "en",
        "de",
        "es",
        "ja",
        "zh-Hans",
    };

    /// <summary>
    /// Resolves the language to use from an explicit request, falling back through the
    /// environment variable and the operating system to English.
    /// </summary>
    /// <param name="requested">
    /// A language tag from a command-line option or a setting, or <c>null</c> when none was given.
    /// The literal string <c>auto</c> is treated the same as <c>null</c>, because that is what the
    /// editor setting stores for "decide for me".
    /// </param>
    /// <returns>A culture that has translations, or the invariant-language English culture.</returns>
    public static CultureInfo Resolve(string? requested)
    {
        if (TryMatch(requested, out var explicitly))
            return explicitly;

        if (TryMatch(Environment.GetEnvironmentVariable(EnvironmentVariable), out var fromEnvironment))
            return fromEnvironment;

        if (TryMatch(CultureInfo.CurrentUICulture.Name, out var fromSystem))
            return fromSystem;

        return new CultureInfo(Default);
    }

    /// <summary>
    /// Matches a language tag against the shipped set, narrowing a regional tag to its language
    /// where there is no regional translation.
    /// </summary>
    /// <remarks>
    /// <c>de-AT</c> resolves to <c>de</c>, and <c>zh-CN</c> reaches <c>zh-Hans</c> through its
    /// parent chain, which is why this walks parents rather than comparing prefixes: the tag for
    /// simplified Chinese shares no prefix with the tag a browser is likely to send.
    /// </remarks>
    /// <param name="tag">A language tag, or <c>null</c>.</param>
    /// <param name="culture">The matched culture, or <c>null</c> when there is no match.</param>
    /// <returns><c>true</c> when the tag maps onto a shipped language.</returns>
    public static bool TryMatch(string? tag, out CultureInfo culture)
    {
        culture = null!;

        if (string.IsNullOrWhiteSpace(tag) || tag.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return false;

        CultureInfo candidate;
        try
        {
            candidate = new CultureInfo(tag.Trim());
        }
        catch (CultureNotFoundException)
        {
            return false;
        }

        // The pseudo-locale is a real translation as far as the resource system is concerned,
        // it is just not one anybody should be offered.
        if (string.Equals(candidate.Name, Pseudo, StringComparison.OrdinalIgnoreCase))
        {
            culture = candidate;
            return true;
        }

        for (var walk = candidate; !string.IsNullOrEmpty(walk.Name); walk = walk.Parent)
        {
            foreach (var supported in Supported)
            {
                if (string.Equals(walk.Name, supported, StringComparison.OrdinalIgnoreCase))
                {
                    culture = new CultureInfo(supported);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the language named by <see cref="Option"/> out of raw process arguments.
    /// </summary>
    /// <remarks>
    /// A scan rather than a parse, because the callers that need this need it before parsing is
    /// possible: command help is written while the command tree is built, and the notebook host
    /// has no parser at all. Both <c>--language de</c> and <c>--language=de</c> are accepted, and
    /// anything else is left alone for a real parser to report in the ordinary way.
    /// </remarks>
    /// <param name="args">The process arguments.</param>
    /// <returns>The requested language tag, or <c>null</c> when the option is absent.</returns>
    public static string? FromArguments(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith(Option + "=", StringComparison.Ordinal))
                return args[i][(Option.Length + 1)..];

            if (args[i].Equals(Option, StringComparison.Ordinal) && i + 1 < args.Count)
                return args[i + 1];
        }

        return null;
    }

    /// <summary>
    /// Applies a language to the interface without touching how numbers and dates are formatted.
    /// </summary>
    /// <remarks>
    /// The two are deliberately separate. A notebook's own results are data, and this process
    /// runs the kernels that produce them, so moving the formatting culture would change a cell's
    /// output because somebody picked a menu language. Hosts that run kernels elsewhere, such as
    /// the editor extension, can afford to move both and do.
    /// </remarks>
    /// <param name="culture">The language to display the interface in.</param>
    public static void ApplyUiCulture(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
