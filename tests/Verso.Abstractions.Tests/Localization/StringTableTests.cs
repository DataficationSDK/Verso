using System.Globalization;
using System.Resources;

namespace Verso.Abstractions.Tests.Localization;

/// <summary>
/// The table an extension hands to its renderer has to say the same thing a generated
/// resource property would say, key for key, including the fallback for a string no
/// translator has reached.
/// </summary>
[TestClass]
public class StringTableTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"verso-stringtable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);

        Write("Probe.resources", ("Greeting", "Hello"), ("Farewell", "Goodbye"), ("Count", "{0} cells"));
        Write("Probe.de.resources", ("Greeting", "Hallo"), ("Count", "{0} Zellen"));
        Write("Probe.de-AT.resources", ("Greeting", "Servus"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private void Write(string fileName, params (string Key, string Value)[] entries)
    {
        using var writer = new ResourceWriter(Path.Combine(_dir, fileName));
        foreach (var (key, value) in entries)
            writer.AddResource(key, value);
    }

    private ResourceManager Manager() =>
        ResourceManager.CreateFileBasedResourceManager("Probe", _dir, usingResourceSet: null);

    [TestMethod]
    public void From_ReturnsEveryNeutralKey()
    {
        var table = StringTable.From(Manager(), CultureInfo.GetCultureInfo("de"));

        CollectionAssert.AreEquivalent(new[] { "Greeting", "Farewell", "Count" }, table.Keys.ToArray());
    }

    [TestMethod]
    public void From_ResolvesEachKeyForTheCulture()
    {
        var table = StringTable.From(Manager(), CultureInfo.GetCultureInfo("de"));

        Assert.AreEqual("Hallo", table["Greeting"]);
        Assert.AreEqual("{0} Zellen", table["Count"], "A placeholder travels with the translation.");
    }

    [TestMethod]
    public void From_FallsBackPerKeyThroughTheParentToNeutral()
    {
        var table = StringTable.From(Manager(), CultureInfo.GetCultureInfo("de-AT"));

        Assert.AreEqual("Servus", table["Greeting"], "The regional set wins where it has the key.");
        Assert.AreEqual("{0} Zellen", table["Count"], "The parent language fills what the region lacks.");
        Assert.AreEqual("Goodbye", table["Farewell"], "The neutral set fills what no translation has.");
    }

    [TestMethod]
    public void From_UntranslatedCulture_ReturnsNeutralValues()
    {
        var table = StringTable.From(Manager(), CultureInfo.GetCultureInfo("fr"));

        Assert.AreEqual("Hello", table["Greeting"]);
        Assert.AreEqual("Goodbye", table["Farewell"]);
    }
}
