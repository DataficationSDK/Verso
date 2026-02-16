using Verso.FSharp.NuGet;

namespace Verso.FSharp.Tests.NuGet;

[TestClass]
public class NuGetFallbackResolverTests
{
    [TestMethod]
    public void ParseNuGetReference_PackageOnly_ReturnsPackageIdAndNullVersion()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("Newtonsoft.Json");
        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.IsNull(result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_WithCommaVersion_ReturnsPackageIdAndVersion()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("Newtonsoft.Json, 13.0.1");
        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_WithSpaceVersion_ReturnsPackageIdAndVersion()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("Newtonsoft.Json 13.0.1");
        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    public void ParseNuGetReference_Null_ReturnsNull()
    {
        Assert.IsNull(NuGetFallbackResolver.ParseNuGetReference(null));
    }

    [TestMethod]
    public void ParseNuGetReference_Empty_ReturnsNull()
    {
        Assert.IsNull(NuGetFallbackResolver.ParseNuGetReference(""));
    }

    [TestMethod]
    public void ParseNuGetReference_Whitespace_ReturnsNull()
    {
        Assert.IsNull(NuGetFallbackResolver.ParseNuGetReference("  "));
    }

    [TestMethod]
    public void ParseNuGetReference_TrimsWhitespace()
    {
        var result = NuGetFallbackResolver.ParseNuGetReference("  Newtonsoft.Json , 13.0.1 ");
        Assert.IsNotNull(result);
        Assert.AreEqual("Newtonsoft.Json", result.Value.PackageId);
        Assert.AreEqual("13.0.1", result.Value.Version);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ResolvePackageAsync_ResolvesRealPackage()
    {
        var resolver = new NuGetFallbackResolver();
        var result = await resolver.ResolvePackageAsync("Newtonsoft.Json", "13.0.3", CancellationToken.None);

        Assert.AreEqual("Newtonsoft.Json", result.PackageId);
        Assert.AreEqual("13.0.3", result.ResolvedVersion);
        Assert.IsTrue(result.AssemblyPaths.Count > 0, "Expected at least one assembly path");
        Assert.IsTrue(
            result.AssemblyPaths.Any(p => p.EndsWith("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase)),
            "Expected Newtonsoft.Json.dll in assembly paths");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ResolvePackageAsync_NonExistentPackage_ThrowsNotFound()
    {
        var resolver = new NuGetFallbackResolver();

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => resolver.ResolvePackageAsync(
                "Verso.ThisPackageDoesNotExist.ZZZZZ", null, CancellationToken.None));

        Assert.IsTrue(ex.Message.Contains("not found on nuget.org"),
            $"Expected 'not found on nuget.org' in message, got: {ex.Message}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ResolvePackageAsync_NonExistentVersion_ThrowsWithAvailableVersions()
    {
        var resolver = new NuGetFallbackResolver();

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => resolver.ResolvePackageAsync(
                "Newtonsoft.Json", "99.99.99", CancellationToken.None));

        Assert.IsTrue(ex.Message.Contains("not found"),
            $"Expected 'not found' in message, got: {ex.Message}");
        Assert.IsTrue(ex.Message.Contains("Available versions"),
            $"Expected 'Available versions' in message, got: {ex.Message}");
    }
}
