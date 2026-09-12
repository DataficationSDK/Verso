using System.Globalization;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// The two values an isolated layout's frame is handed before any of its own code runs: the
/// protocol version the host compares against, and the language tag it is told it is drawing in.
/// Both are read by renderers written outside this repository, so both are contracts.
/// </summary>
[TestClass]
public sealed class CustomLayoutFrameContractTests
{
    [TestMethod]
    public void ProtocolIsCompatible_OlderMinor_IsAccepted()
    {
        // A renderer built against an older minor only lacks fields the host now sends.
        Assert.IsTrue(CustomLayoutFrame.ProtocolIsCompatible("1.0", "1.1"));
        Assert.IsTrue(CustomLayoutFrame.ProtocolIsCompatible("1.1", "1.1"));
    }

    [TestMethod]
    public void ProtocolIsCompatible_NewerMinorOrOtherMajor_IsRejected()
    {
        Assert.IsFalse(CustomLayoutFrame.ProtocolIsCompatible("1.2", "1.1"),
            "A renderer built against a newer minor may expect fields this host does not send.");
        Assert.IsFalse(CustomLayoutFrame.ProtocolIsCompatible("2.0", "1.1"),
            "A different major is a different contract.");
    }

    [TestMethod]
    public void ProtocolIsCompatible_PatchComponent_IsIgnoredRatherThanRejected()
    {
        // Only major.minor carries meaning, so "1.0.0" says exactly what "1.0" says. Reading the
        // whole remainder after the first dot would fail to parse and refuse a working renderer.
        Assert.IsTrue(CustomLayoutFrame.ProtocolIsCompatible("1.0.0", "1.1"));
        Assert.IsTrue(CustomLayoutFrame.ProtocolIsCompatible("1.1.7", "1.1"));
        Assert.IsFalse(CustomLayoutFrame.ProtocolIsCompatible("1.2.0", "1.1"));
    }

    [TestMethod]
    public void ProtocolIsCompatible_NotAVersion_IsRejected()
    {
        Assert.IsFalse(CustomLayoutFrame.ProtocolIsCompatible("1", "1.1"));
        Assert.IsFalse(CustomLayoutFrame.ProtocolIsCompatible("", "1.1"));
        Assert.IsFalse(CustomLayoutFrame.ProtocolIsCompatible("next", "1.1"));
        Assert.IsFalse(CustomLayoutFrame.ProtocolIsCompatible("-1.0", "1.1"),
            "A sign is not part of a version.");
    }

    [TestMethod]
    public void ProtocolIsCompatible_ReaderInAnotherLocale_ReadsTheSameVersion()
    {
        // A version is punctuation, not a number the reader's locale gets a say in. Under a
        // locale that writes decimals with a comma, a culture-sensitive parse of "1.1" can
        // read one thousand one hundred and refuse every renderer.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Assert.IsTrue(CustomLayoutFrame.ProtocolIsCompatible("1.0", "1.1"));
            Assert.IsFalse(CustomLayoutFrame.ProtocolIsCompatible("1.2", "1.1"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void CurrentCultureTag_ResolvedLanguage_IsPassedThrough()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.AreEqual("de", CustomLayoutFrame.CurrentCultureTag());

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-Hans");
            Assert.AreEqual("zh-Hans", CustomLayoutFrame.CurrentCultureTag());
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [TestMethod]
    public void CurrentCultureTag_NoLanguageResolved_FallsBackToEnglish()
    {
        // The invariant culture's name is the empty string. Sending it would write
        // <html lang=""> into the frame and give a renderer nothing to switch on.
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.AreEqual("en", CustomLayoutFrame.CurrentCultureTag());
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
