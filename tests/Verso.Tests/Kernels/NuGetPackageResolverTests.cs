using Verso.Kernels;

namespace Verso.Tests.Kernels;

[TestClass]
public sealed class NuGetPackageResolverTests
{
    [TestMethod]
    public void ParseNuGetReference_PackageOnly()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.IsNull(result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_PackageAndVersion_CommaSeparated()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json, 13.0.1");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_PackageAndVersion_SpaceSeparated()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json 13.0.1");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_WithWhitespace_TrimsCorrectly()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("  Newtonsoft.Json , 13.0.1  ");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_EmptyString_ReturnsNull()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseNuGetReference_NullString_ReturnsNull()
    {
        var result = NuGetPackageResolver.ParseNuGetReference(null);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseNuGetReference_WhitespaceOnly_ReturnsNull()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("   ");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseNuGetReference_CommaThenEmpty_VersionIsNull()
    {
        var result = NuGetPackageResolver.ParseNuGetReference("Newtonsoft.Json,");

        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.IsNull(result.Value.Version);
    }
}
