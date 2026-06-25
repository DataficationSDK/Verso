using Verso.Extensions.Marketplace;

namespace Verso.Tests.Extensions;

[TestClass]
public class ExtensionPackageRefTests
{
    [TestMethod]
    public void Parse_IdWithVersion_SplitsBoth()
    {
        var (id, version, source) = ExtensionPackageRef.Parse("My.Extension@1.2.3");
        Assert.AreEqual("My.Extension", id);
        Assert.AreEqual("1.2.3", version);
        Assert.AreEqual(ExtensionSource.NuGet, source);
    }

    [TestMethod]
    public void Parse_IdOnly_VersionIsNull()
    {
        var (id, version, source) = ExtensionPackageRef.Parse("My.Extension");
        Assert.AreEqual("My.Extension", id);
        Assert.IsNull(version);
        Assert.AreEqual(ExtensionSource.NuGet, source);
    }

    [TestMethod]
    public void Parse_PrereleaseVersion_PreservedAfterFirstAt()
    {
        var (id, version, _) = ExtensionPackageRef.Parse("My.Extension@1.2.3-beta.1");
        Assert.AreEqual("My.Extension", id);
        Assert.AreEqual("1.2.3-beta.1", version);
    }

    [TestMethod]
    public void Parse_TrailingAtWithNoVersion_VersionIsNull()
    {
        var (id, version, _) = ExtensionPackageRef.Parse("My.Extension@");
        Assert.AreEqual("My.Extension", id);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void Parse_Whitespace_TrimsAndReturnsEmpty()
    {
        var (id, version, source) = ExtensionPackageRef.Parse("   ");
        Assert.AreEqual(string.Empty, id);
        Assert.IsNull(version);
        Assert.AreEqual(ExtensionSource.NuGet, source);
    }

    [TestMethod]
    public void Parse_LocalPrefix_MarksLocalAndStripsScheme()
    {
        var (id, version, source) = ExtensionPackageRef.Parse("local:My.Extension@1.0.0");
        Assert.AreEqual("My.Extension", id);
        Assert.AreEqual("1.0.0", version);
        Assert.AreEqual(ExtensionSource.Local, source);
    }

    [TestMethod]
    public void Parse_LocalPrefix_IsCaseInsensitive()
    {
        var (id, _, source) = ExtensionPackageRef.Parse("LOCAL:My.Extension@1.0.0");
        Assert.AreEqual("My.Extension", id);
        Assert.AreEqual(ExtensionSource.Local, source);
    }

    [TestMethod]
    public void ParseId_StripsLocalSchemeAndVersion()
    {
        Assert.AreEqual("My.Extension", ExtensionPackageRef.ParseId("local:My.Extension@9.9.9"));
        Assert.AreEqual("My.Extension", ExtensionPackageRef.ParseId("My.Extension@9.9.9"));
    }

    [TestMethod]
    public void Format_WithVersion_JoinsWithAt()
    {
        Assert.AreEqual("My.Extension@1.0.0", ExtensionPackageRef.Format("My.Extension", "1.0.0"));
    }

    [TestMethod]
    public void Format_NullVersion_ReturnsIdOnly()
    {
        Assert.AreEqual("My.Extension", ExtensionPackageRef.Format("My.Extension", null));
    }

    [TestMethod]
    public void Format_LocalSource_AddsScheme()
    {
        Assert.AreEqual("local:My.Extension@1.0.0",
            ExtensionPackageRef.Format("My.Extension", "1.0.0", ExtensionSource.Local));
    }

    [TestMethod]
    public void Format_ThenParse_RoundTrips()
    {
        var formatted = ExtensionPackageRef.Format("Pkg.Id", "2.5.0");
        var (id, version, source) = ExtensionPackageRef.Parse(formatted);
        Assert.AreEqual("Pkg.Id", id);
        Assert.AreEqual("2.5.0", version);
        Assert.AreEqual(ExtensionSource.NuGet, source);
    }

    [TestMethod]
    public void Format_ThenParse_LocalRoundTrips()
    {
        var formatted = ExtensionPackageRef.Format("Pkg.Id", "2.5.0", ExtensionSource.Local);
        var (id, version, source) = ExtensionPackageRef.Parse(formatted);
        Assert.AreEqual("Pkg.Id", id);
        Assert.AreEqual("2.5.0", version);
        Assert.AreEqual(ExtensionSource.Local, source);
    }

    // Locks the projection the extension panel's installed list relies on: a mixed
    // required-extensions list maps to (id, version, isLocal) with blanks dropped.
    [TestMethod]
    public void Parse_MixedRequiredList_ProjectsInstalledEntries()
    {
        string[] required = { "Nuget.Pkg@1.0.0", "local:Side.Loaded@2.1.0", "Latest.Pkg", "   " };

        var projected = required
            .Select(r =>
            {
                var (id, version, source) = ExtensionPackageRef.Parse(r);
                return (id, version, isLocal: source == ExtensionSource.Local);
            })
            .Where(e => !string.IsNullOrEmpty(e.id))
            .ToList();

        Assert.AreEqual(3, projected.Count);
        CollectionAssert.AreEqual(
            new[] { "Nuget.Pkg", "Side.Loaded", "Latest.Pkg" },
            projected.Select(e => e.id).ToArray());
        Assert.IsFalse(projected[0].isLocal);
        Assert.AreEqual("1.0.0", projected[0].version);
        Assert.IsTrue(projected[1].isLocal, "the local:-prefixed entry must project as local");
        Assert.AreEqual("2.1.0", projected[1].version);
        Assert.IsFalse(projected[2].isLocal);
        Assert.IsNull(projected[2].version, "a bare id has no pinned version");
    }
}
