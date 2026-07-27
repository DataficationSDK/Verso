using Verso.Python.PackageManagement;

namespace Verso.Python.Tests.PackageManagement;

/// <summary>
/// Covers how a module name becomes a distribution name, and whether that answer counts as known.
/// The distinction matters: an unknown name is never installed without asking, because a
/// misspelled import resolves to a plausible package name somebody may have published.
/// </summary>
[TestClass]
public sealed class ImportMapTests
{
    [TestMethod]
    public void Resolve_UsesTheBuiltInTableWhereTheNamesDiffer()
    {
        var resolved = ImportMap.Resolve("cv2");

        Assert.AreEqual("opencv-python", resolved.Distribution);
        Assert.IsTrue(resolved.Vetted);
    }

    [TestMethod]
    public void Resolve_KeepsTheModuleNameWhenNothingKnowsBetter()
    {
        var resolved = ImportMap.Resolve("pandas");

        Assert.AreEqual("pandas", resolved.Distribution);
        Assert.IsFalse(resolved.Vetted, "An assumed name is a guess, not a known mapping.");
    }

    [TestMethod]
    public void Resolve_MarksAMisspellingAsAGuess()
    {
        // The typosquatting case: the name looks installable and nobody vouched for it.
        var resolved = ImportMap.Resolve("pandsa");

        Assert.AreEqual("pandsa", resolved.Distribution);
        Assert.IsFalse(resolved.Vetted);
    }

    [TestMethod]
    public void Resolve_PrefersAConfiguredEntryOverTheBuiltInTable()
    {
        var userMap = new Dictionary<string, string> { ["cv2"] = "opencv-python-headless" };

        var resolved = ImportMap.Resolve("cv2", userMap);

        Assert.AreEqual("opencv-python-headless", resolved.Distribution);
        Assert.IsTrue(resolved.Vetted);
    }

    [TestMethod]
    public void Resolve_TreatsAConfiguredEntryAsKnown()
    {
        var userMap = new Dictionary<string, string> { ["housetools"] = "acme-house-tools" };

        var resolved = ImportMap.Resolve("housetools", userMap);

        Assert.AreEqual("acme-house-tools", resolved.Distribution);
        Assert.IsTrue(resolved.Vetted, "Configuring a mapping is how a user vouches for it.");
    }

    [TestMethod]
    public void Resolve_IgnoresAConfiguredEntryWithNoDistribution()
    {
        var userMap = new Dictionary<string, string> { ["cv2"] = "   " };

        var resolved = ImportMap.Resolve("cv2", userMap);

        Assert.AreEqual("opencv-python", resolved.Distribution);
    }

    [TestMethod]
    public void Resolve_ReducesADottedNameToItsRoot()
    {
        var resolved = ImportMap.Resolve("PIL.Image");

        Assert.AreEqual("PIL", resolved.Module);
        Assert.AreEqual("Pillow", resolved.Distribution);
    }

    [TestMethod]
    public void Resolve_IsCaseSensitiveBecausePythonModulesAre()
    {
        // PIL and pil are different modules, and only one of them exists.
        Assert.IsTrue(ImportMap.Resolve("PIL").Vetted);
        Assert.IsFalse(ImportMap.Resolve("pil").Vetted);
    }

    [TestMethod]
    public void RootOf_HandlesAPlainName()
    {
        Assert.AreEqual("json", ImportMap.RootOf("json"));
        Assert.AreEqual("", ImportMap.RootOf(""));
    }

    [TestMethod]
    public void Table_CoversTheCommonlyMissedPackages()
    {
        foreach (var module in new[] { "cv2", "PIL", "sklearn", "bs4", "yaml", "dotenv", "dateutil" })
            Assert.IsTrue(ImportMap.IsCurated(module), $"'{module}' should have a known mapping.");
    }

    [TestMethod]
    public void Table_HasNoEntryThatOnlyRepeatsTheModuleName()
    {
        // An identity entry would claim a name is known when the resolver would have produced
        // the same answer by guessing, which changes whether it installs without asking.
        foreach (var module in new[] { "pandas", "numpy", "requests", "flask" })
            Assert.IsFalse(ImportMap.IsCurated(module), $"'{module}' needs no mapping.");
    }
}
