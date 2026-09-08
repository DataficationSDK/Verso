using System.Globalization;
using System.Text.Json;

namespace Verso.Showcase.FormStudio.Tests;

/// <summary>
/// A widget label has two audiences that want different things. The canvas wants it in the
/// language the reader is working in; the notebook wants it in no language at all, so that the
/// file opens correctly for the next person. These tests hold those two apart.
/// </summary>
/// <remarks>
/// The pseudo-locale is used because it exists whether or not a translator has reached these
/// strings yet, so the tests do not go quiet the moment a language is added or dropped.
/// </remarks>
[TestClass]
public sealed class WidgetLabelTests
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

    private static string LabelOf(string document, string widgetId)
    {
        using var parsed = JsonDocument.Parse(document);
        foreach (var widget in parsed.RootElement.GetProperty("widgets").EnumerateArray())
        {
            if (widget.GetProperty("id").GetString() == widgetId)
                return widget.TryGetProperty("label", out var label) ? label.GetString() ?? "" : "";
        }

        throw new AssertFailedException($"No widget with id '{widgetId}'.");
    }

    [TestMethod]
    public void SeededDashboard_StoresKeysRatherThanLabels()
    {
        var stored = (string)new FormDocument().ToMetadata()["doc"];

        StringAssert.Contains(stored, "\"labelKey\":\"Seed_MinimumUnits\"");
        Assert.IsFalse(stored.Contains("\"label\":"),
            "A built-in label was written into the notebook, which freezes the file in the language it was saved from.");
    }

    [TestMethod]
    public void ForDisplay_ResolvesLabelsAndKeepsTheKeys()
    {
        var document = new FormDocument();

        var english = document.ForDisplay();
        var translated = InPseudoLocale(document.ForDisplay);

        Assert.AreNotEqual(LabelOf(english, "w_slider"), LabelOf(translated, "w_slider"),
            "The seed resolved its labels once and kept them, so a reader in another language sees the first reader's.");
        StringAssert.Contains(translated, "\"labelKey\":\"Seed_MinimumUnits\"",
            "The key has to survive the trip so the frame can hand it back.");
        Assert.AreEqual(LabelOf(english, "w_slider"), LabelOf(document.ForDisplay(), "w_slider"),
            "The label did not come back after the language did.");
    }

    [TestMethod]
    public void SavingBackAResolvedLabel_DoesNotBakeTheLanguageIn()
    {
        // What the frame sends after any edit: the whole canvas, including the label it drew.
        // A widget that still names a key never had its label typed by anyone, so the resolved
        // text is dropped rather than stored.
        var document = new FormDocument();
        var asDrawn = InPseudoLocale(document.ForDisplay);

        InPseudoLocale(() => { document.Update(asDrawn); return 0; });

        var stored = (string)document.ToMetadata()["doc"];
        StringAssert.Contains(stored, "\"labelKey\":\"Seed_MinimumUnits\"");
        Assert.IsFalse(stored.Contains("\"label\":"),
            "The frame's resolved label was trusted and stored, which freezes the notebook in that reader's language.");
    }

    [TestMethod]
    public void TypedLabel_ReplacesTheKeyForGood()
    {
        // The frame clears labelKey when someone types a label, which is how it says the label
        // is now theirs rather than a built-in one.
        var document = new FormDocument();
        var edited = """
            {"autoRun":true,"widgets":[{"id":"w_slider","kind":"slider","label":"Units floor","bindVar":"minUnits"}]}
            """;

        document.Update(edited);

        var stored = (string)document.ToMetadata()["doc"];
        StringAssert.Contains(stored, "\"label\":\"Units floor\"");
        Assert.AreEqual("Units floor", LabelOf(InPseudoLocale(document.ForDisplay), "w_slider"),
            "A label a person chose is theirs and is not translated out from under them.");
    }

    [TestMethod]
    public void ComposedChartLabel_MovesBothHalvesTogether()
    {
        // A chart's default title is "{0} chart" over a chart-kind word. Storing the composed
        // text would translate half of it; storing both keys keeps them in step.
        var document = new FormDocument();
        document.Update("""
            {"autoRun":true,"widgets":[{"id":"w_c","kind":"chart","labelKey":"Widget_ChartLabel","labelArgKey":"Palette_Bar","config":{}}]}
            """);

        var english = LabelOf(document.ForDisplay(), "w_c");
        var translated = LabelOf(InPseudoLocale(document.ForDisplay), "w_c");

        Assert.AreEqual("Bar chart", english);
        Assert.AreNotEqual(english, translated);
        Assert.IsFalse(translated.Contains("Bar", StringComparison.Ordinal),
            "The chart-kind word was left in English inside a translated title.");
    }

    [TestMethod]
    public void UnknownKey_LeavesTheWidgetAloneRatherThanThrowing()
    {
        // A document written by a newer build can name a key this one has never heard of.
        var document = new FormDocument();
        document.Update("""
            {"autoRun":true,"widgets":[{"id":"w_x","kind":"slider","labelKey":"Seed_NotShippedYet"}]}
            """);

        Assert.AreEqual("", LabelOf(document.ForDisplay(), "w_x"));
    }

    [TestMethod]
    public void UnreadableDocument_IsLeftAsItIs()
    {
        var document = new FormDocument();
        var before = document.ForDisplay();

        document.Update("{ not json");

        Assert.AreEqual(before, document.ForDisplay(),
            "An unparseable payload replaced a good document instead of being refused.");
    }
}
