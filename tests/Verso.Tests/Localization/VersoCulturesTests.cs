using System.Globalization;
using Verso.Localization;

namespace Verso.Tests.Localization;

[TestClass]
public class VersoCulturesTests
{
    private CultureInfo _originalUiCulture = null!;
    private string? _originalEnvironment;

    [TestInitialize]
    public void Setup()
    {
        _originalUiCulture = CultureInfo.CurrentUICulture;
        _originalEnvironment = Environment.GetEnvironmentVariable(VersoCultures.EnvironmentVariable);
        Environment.SetEnvironmentVariable(VersoCultures.EnvironmentVariable, null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(VersoCultures.EnvironmentVariable, _originalEnvironment);
        CultureInfo.CurrentUICulture = _originalUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = null;
    }

    [TestMethod]
    public void Supported_LeadsWithTheLanguageEverythingIsWrittenIn()
    {
        Assert.AreEqual(VersoCultures.Default, VersoCultures.Supported[0]);
    }

    [TestMethod]
    public void Supported_DoesNotOfferThePseudoLocale()
    {
        CollectionAssert.DoesNotContain(VersoCultures.Supported.ToList(), VersoCultures.Pseudo);
    }

    [TestMethod]
    public void TryMatch_AcceptsAShippedLanguage()
    {
        Assert.IsTrue(VersoCultures.TryMatch("de", out var culture));
        Assert.AreEqual("de", culture.Name);
    }

    [TestMethod]
    public void TryMatch_NarrowsARegionToItsLanguage()
    {
        Assert.IsTrue(VersoCultures.TryMatch("de-AT", out var culture));
        Assert.AreEqual("de", culture.Name);
    }

    [TestMethod]
    public void TryMatch_ReachesSimplifiedChineseThroughTheParentChain()
    {
        // zh-CN shares no prefix with zh-Hans, so a prefix comparison would miss it. This is the
        // tag a browser or an editor is most likely to send for simplified Chinese.
        Assert.IsTrue(VersoCultures.TryMatch("zh-CN", out var culture));
        Assert.AreEqual("zh-Hans", culture.Name);
    }

    [TestMethod]
    public void TryMatch_AcceptsThePseudoLocaleByName()
    {
        Assert.IsTrue(VersoCultures.TryMatch("qps-ploc", out var culture));
        Assert.AreEqual(VersoCultures.Pseudo, culture.Name);
    }

    [TestMethod]
    public void TryMatch_RejectsALanguageWithNoTranslation()
    {
        Assert.IsFalse(VersoCultures.TryMatch("fi", out _));
    }

    [TestMethod]
    public void TryMatch_RejectsNonsense()
    {
        Assert.IsFalse(VersoCultures.TryMatch("not-a-language-tag", out _));
    }

    [TestMethod]
    public void TryMatch_TreatsAutoAsNoAnswer()
    {
        Assert.IsFalse(VersoCultures.TryMatch("auto", out _));
    }

    [TestMethod]
    public void TryMatch_TreatsNullAndBlankAsNoAnswer()
    {
        Assert.IsFalse(VersoCultures.TryMatch(null, out _));
        Assert.IsFalse(VersoCultures.TryMatch("   ", out _));
    }

    [TestMethod]
    public void Resolve_PrefersTheExplicitRequest()
    {
        Environment.SetEnvironmentVariable(VersoCultures.EnvironmentVariable, "ja");
        CultureInfo.CurrentUICulture = new CultureInfo("es");

        Assert.AreEqual("de", VersoCultures.Resolve("de").Name);
    }

    [TestMethod]
    public void Resolve_FallsBackToTheEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable(VersoCultures.EnvironmentVariable, "ja");
        CultureInfo.CurrentUICulture = new CultureInfo("es");

        Assert.AreEqual("ja", VersoCultures.Resolve(null).Name);
    }

    [TestMethod]
    public void Resolve_FallsBackToTheSystemLanguage()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("es-MX");

        Assert.AreEqual("es", VersoCultures.Resolve(null).Name);
    }

    [TestMethod]
    public void Resolve_FallsBackToEnglishWhenNothingMatches()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("fi-FI");

        Assert.AreEqual(VersoCultures.Default, VersoCultures.Resolve("nope").Name);
    }

    [TestMethod]
    public void FromArguments_ReadsASeparateValue()
    {
        Assert.AreEqual("de", VersoCultures.FromArguments(new[] { "serve", "--language", "de" }));
    }

    [TestMethod]
    public void FromArguments_ReadsAnAttachedValue()
    {
        Assert.AreEqual("zh-Hans", VersoCultures.FromArguments(new[] { "--language=zh-Hans", "run" }));
    }

    [TestMethod]
    public void FromArguments_IgnoresAnOptionWithNothingAfterIt()
    {
        Assert.IsNull(VersoCultures.FromArguments(new[] { "run", "--language" }));
    }

    [TestMethod]
    public void FromArguments_AnswersNothingWhenTheOptionIsAbsent()
    {
        Assert.IsNull(VersoCultures.FromArguments(new[] { "run", "notebook.verso", "--verbose" }));
    }

    [TestMethod]
    public void ApplyUiCulture_LeavesNumberFormattingAlone()
    {
        // A cell's results are data. Picking a menu language must not change how this process
        // renders the numbers a kernel produced.
        var before = CultureInfo.CurrentCulture.Name;

        VersoCultures.ApplyUiCulture(new CultureInfo("de"));

        Assert.AreEqual("de", CultureInfo.CurrentUICulture.Name);
        Assert.AreEqual(before, CultureInfo.CurrentCulture.Name);
    }
}
