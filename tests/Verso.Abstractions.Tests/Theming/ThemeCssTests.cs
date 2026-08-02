using System.Globalization;
using System.Text.RegularExpressions;

namespace Verso.Abstractions.Tests.Theming;

[TestClass]
public class ThemeCssTests
{
    /// <summary>
    /// Runs <paramref name="body"/> with the formatting culture set to one that writes decimals
    /// with a comma, then puts the original back.
    /// </summary>
    /// <remarks>
    /// Only the formatting culture moves. The interface language is pinned to English for the
    /// whole run, and this is not about language.
    /// </remarks>
    private static void InGerman(Action body)
    {
        var original = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            body();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void EveryNumericToken_IsWrittenTheWayCssReadsThem()
    {
        // A stylesheet is not written in anyone's language: a length is always "1.4", never
        // "1,4", whoever is reading it. The editor boots the notebook interface with a culture,
        // and that moves how numbers are written as well as which language the interface is in,
        // so without this every notebook and every exported document in a comma-decimal
        // language loses its line heights, font sizes and spacing, because a browser discards
        // a declaration it cannot parse.
        //
        // This asserts over the whole block rather than a few named tokens, so a token added
        // later is covered without anyone remembering to come back here.
        InGerman(() =>
        {
            var css = ThemeCss.BuildRootBlock(
                new ThemeColorTokens(),
                new ThemeTypography { FontSizeBase = 14.5 },
                new ThemeSpacing(),
                new ThemeElevation());

            var offenders = Regex
                .Matches(css, @"^\s*(--verso-[a-z0-9-]+):\s*(.+);$", RegexOptions.Multiline)
                .Where(m => Regex.IsMatch(m.Groups[2].Value, @"^-?\d+,\d"))
                .Select(m => m.Groups[0].Value.Trim())
                .ToList();

            Assert.AreEqual(
                0,
                offenders.Count,
                "These tokens were written with a decimal comma, which CSS cannot parse: "
                    + string.Join(" | ", offenders));
        });
    }

    [TestMethod]
    public void NumericTokens_KeepTheirValue()
    {
        // The guard above only proves nothing carries a comma. This proves the numbers are
        // still the right numbers, so replacing them with something like "0" would not pass.
        InGerman(() =>
        {
            var css = ThemeCss.BuildRootBlock(
                new ThemeColorTokens(),
                new ThemeTypography { FontSizeBase = 14.5 },
                new ThemeSpacing(),
                new ThemeElevation());

            StringAssert.Contains(css, "--verso-font-size-base: 14.5px;");
            StringAssert.Contains(css, "--verso-editor-font-line-height: 1.4;");
        });
    }

    [TestMethod]
    public void Typography_EmitsTheTokensThatAreNotFonts()
    {
        // The export used to reflect only over FontDescriptor properties, so these three were
        // absent from every exported document while the live interface had them.
        var css = ThemeCss.BuildRootBlock(null, new ThemeTypography(), null, null);

        StringAssert.Contains(css, "--verso-font-family-mono:");
        StringAssert.Contains(css, "--verso-font-family-sans:");
        StringAssert.Contains(css, "--verso-font-size-base:");
    }

    [TestMethod]
    public void NullTheme_FallsBackToTheDefaultTokens()
    {
        var css = ThemeCss.BuildRootBlock((ITheme?)null);

        StringAssert.StartsWith(css, ":root {");
        StringAssert.Contains(css, "--verso-editor-font-family: Cascadia Code;");
        StringAssert.Contains(css, "--verso-elevation-0: none;");
    }

    [TestMethod]
    public void ElevationTokens_DropTheLevelPrefix()
    {
        var css = ThemeCss.BuildRootBlock(null, null, null, new ThemeElevation());

        StringAssert.Contains(css, "--verso-elevation-1:");
        Assert.IsFalse(css.Contains("--verso-elevation-level", StringComparison.Ordinal));
    }

    private static string BuildRootBlockOverload(ThemeTypography typography) =>
        ThemeCss.BuildRootBlock(null, typography, null, null);

    [TestMethod]
    public void BuildRootBlock_IsStableAcrossCultures()
    {
        // The same tokens in and the same text out, whatever the machine is set to. This is the
        // property the export depends on: `verso export` never pins the formatting culture, so
        // it runs in whatever the operating system says.
        var typography = new ThemeTypography { FontSizeBase = 14.5 };

        var invariant = BuildRootBlockOverload(typography);
        string german = "";
        InGerman(() => german = BuildRootBlockOverload(typography));

        Assert.AreEqual(invariant, german);
    }
}
