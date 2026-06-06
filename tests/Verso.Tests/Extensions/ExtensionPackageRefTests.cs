using Verso.Extensions.Marketplace;

namespace Verso.Tests.Extensions;

[TestClass]
public class ExtensionPackageRefTests
{
    [TestMethod]
    public void Parse_IdWithVersion_SplitsBoth()
    {
        var (id, version) = ExtensionPackageRef.Parse("My.Extension@1.2.3");
        Assert.AreEqual("My.Extension", id);
        Assert.AreEqual("1.2.3", version);
    }

    [TestMethod]
    public void Parse_IdOnly_VersionIsNull()
    {
        var (id, version) = ExtensionPackageRef.Parse("My.Extension");
        Assert.AreEqual("My.Extension", id);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void Parse_PrereleaseVersion_PreservedAfterFirstAt()
    {
        var (id, version) = ExtensionPackageRef.Parse("My.Extension@1.2.3-beta.1");
        Assert.AreEqual("My.Extension", id);
        Assert.AreEqual("1.2.3-beta.1", version);
    }

    [TestMethod]
    public void Parse_TrailingAtWithNoVersion_VersionIsNull()
    {
        var (id, version) = ExtensionPackageRef.Parse("My.Extension@");
        Assert.AreEqual("My.Extension", id);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void Parse_Whitespace_TrimsAndReturnsEmpty()
    {
        var (id, version) = ExtensionPackageRef.Parse("   ");
        Assert.AreEqual(string.Empty, id);
        Assert.IsNull(version);
    }

    [TestMethod]
    public void ParseId_ReturnsIdPortion()
    {
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
    public void Format_ThenParse_RoundTrips()
    {
        var formatted = ExtensionPackageRef.Format("Pkg.Id", "2.5.0");
        var (id, version) = ExtensionPackageRef.Parse(formatted);
        Assert.AreEqual("Pkg.Id", id);
        Assert.AreEqual("2.5.0", version);
    }
}
