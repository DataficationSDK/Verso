using System.Globalization;
using System.Text.Json;
using Verso.Showcase.ImageStudio.Model;

namespace Verso.Showcase.ImageStudio.Tests;

/// <summary>
/// A layer name has two audiences that want different things. The panel wants it in the
/// language the reader is working in; the notebook wants it in no language at all, so that the
/// file opens correctly for the next person. These tests hold those two apart.
/// </summary>
/// <remarks>
/// The pseudo-locale is used because it exists whether or not a translator has reached these
/// strings yet, so the tests do not go quiet the moment a language is added or dropped.
/// </remarks>
[TestClass]
public sealed class LayerNameTests
{
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-Ploc");

    private static T InPseudoLocale<T>(Func<T> read)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = Pseudo;
        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [TestMethod]
    public void SeededLayer_FollowsTheCurrentLanguage()
    {
        var document = LayerDocument.Seed();
        var layer = document.Layers[0];

        var english = layer.DisplayName;
        var translated = InPseudoLocale(() => layer.DisplayName);

        Assert.AreNotEqual(english, translated,
            "The seed resolved its names once and kept them, so a reader in another language sees the first reader's.");
        Assert.AreEqual(english, layer.DisplayName, "The name did not come back after the language did.");
    }

    [TestMethod]
    public void SeededLayer_StoresAKeyRatherThanAName()
    {
        // ToMetadata is what actually reaches the file, and it drops null properties, so a
        // built-in name leaves no trace beyond its key.
        var stored = (string)LayerDocument.Seed().ToMetadata()["document"];

        StringAssert.Contains(stored, "\"nameKey\":\"Seed_Sky\"");
        Assert.IsFalse(stored.Contains("\"name\":"),
            "A built-in name was written into the notebook, which freezes the file in the language it was saved from.");
    }

    [TestMethod]
    public void RenamedLayer_KeepsTheNameThatWasTyped()
    {
        var layer = new Layer { NameKey = "Seed_Sky" };

        layer.Name = "Backdrop";
        layer.NameKey = null;

        Assert.AreEqual("Backdrop", layer.DisplayName);
        Assert.AreEqual("Backdrop", InPseudoLocale(() => layer.DisplayName),
            "A name a person chose is theirs and is not translated out from under them.");
    }

    [TestMethod]
    public void UnknownKey_FallsBackRatherThanThrowing()
    {
        // A document written by a newer build can name a key this one has never heard of.
        var layer = new Layer { NameKey = "Seed_NotShippedYet" };

        Assert.AreEqual(new Layer().DisplayName, layer.DisplayName);
    }

    [TestMethod]
    public void ForDisplay_SendsResolvedNamesAndNoKeys()
    {
        var document = LayerDocument.Seed();

        var payload = InPseudoLocale(() => JsonSerializer.Serialize(document.ForDisplay()));

        Assert.IsFalse(payload.Contains("nameKey"),
            "The frame has no resource manager, so a key would reach the panel as literal text.");
        Assert.IsFalse(payload.Contains("Seed_Sky"), "The frame was sent a key instead of a name.");
        StringAssert.Contains(payload, "\"name\":");
    }

    [TestMethod]
    public void RoundTrip_ThroughMetadata_KeepsTheLanguageOut()
    {
        // Save in one language, open in another: the panel follows the reader, not the author.
        var saved = InPseudoLocale(() => LayerDocument.Seed().ToMetadata());

        var reopened = LayerDocument.FromMetadata(saved);

        Assert.AreEqual(LayerDocument.Seed().Layers[0].DisplayName, reopened.Layers[0].DisplayName);
    }
}
