using System.Reflection;
using Verso.Extensions;
using Verso.Extensions.Marketplace;

namespace Verso.Tests.Extensions;

[TestClass]
public class NuGetMarketplaceLocalInstallTests
{
    private string _managedDir = null!;
    private static string SampleDllPath => typeof(NuGetMarketplaceService).Assembly.Location;

    [TestInitialize]
    public void Setup()
    {
        _managedDir = Path.Combine(Path.GetTempPath(), $"verso-managed-test-{Guid.NewGuid():N}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_managedDir))
                Directory.Delete(_managedDir, recursive: true);
        }
        catch { /* best effort */ }
    }

    [TestMethod]
    public void PeekLocalIdentity_Dll_ReturnsAssemblyNameAndVersion()
    {
        var identity = NuGetMarketplaceService.PeekLocalIdentity(SampleDllPath);

        Assert.IsNotNull(identity);
        var expectedName = AssemblyName.GetAssemblyName(SampleDllPath).Name;
        Assert.AreEqual(expectedName, identity.Value.Id);
        Assert.IsFalse(string.IsNullOrWhiteSpace(identity.Value.Version));
    }

    [TestMethod]
    public void PeekLocalIdentity_MissingFile_ReturnsNull()
    {
        Assert.IsNull(NuGetMarketplaceService.PeekLocalIdentity(Path.Combine(_managedDir, "nope.dll")));
    }

    [TestMethod]
    public void PeekLocalIdentity_UnsupportedExtension_ReturnsNull()
    {
        var txt = Path.Combine(Path.GetTempPath(), $"verso-{Guid.NewGuid():N}.txt");
        File.WriteAllText(txt, "not an assembly");
        try
        {
            Assert.IsNull(NuGetMarketplaceService.PeekLocalIdentity(txt));
        }
        finally
        {
            File.Delete(txt);
        }
    }

    [TestMethod]
    public async Task InstallFromFileAsync_Dll_CopiesIntoManagedVersionDir()
    {
        var service = new NuGetMarketplaceService();

        var result = await service.InstallFromFileAsync(SampleDllPath, _managedDir, CancellationToken.None);

        var expectedName = AssemblyName.GetAssemblyName(SampleDllPath).Name!;
        Assert.AreEqual(expectedName, result.PackageId);
        Assert.AreEqual(Path.Combine(_managedDir, result.PackageId, result.ResolvedVersion), result.PackageDirectory);
        Assert.AreEqual(1, result.AssemblyPaths.Count);
        Assert.IsTrue(File.Exists(result.AssemblyPaths[0]));
    }

    [TestMethod]
    public async Task InstallFromFileAsync_UnsupportedExtension_Throws()
    {
        var service = new NuGetMarketplaceService();
        var txt = Path.Combine(Path.GetTempPath(), $"verso-{Guid.NewGuid():N}.txt");
        File.WriteAllText(txt, "nope");
        try
        {
            await Assert.ThrowsExceptionAsync<NotSupportedException>(
                () => service.InstallFromFileAsync(txt, _managedDir, CancellationToken.None));
        }
        finally
        {
            File.Delete(txt);
        }
    }

    [TestMethod]
    public async Task TryResolveInstalled_FindsPreviouslyInstalledDll()
    {
        var service = new NuGetMarketplaceService();
        var install = await service.InstallFromFileAsync(SampleDllPath, _managedDir, CancellationToken.None);

        var resolved = service.TryResolveInstalled(install.PackageId, install.ResolvedVersion, _managedDir);

        Assert.IsNotNull(resolved);
        Assert.AreEqual(install.PackageDirectory, resolved.PackageDirectory);
        Assert.IsTrue(resolved.AssemblyPaths.Count >= 1);
    }

    [TestMethod]
    public void TryResolveInstalled_MissingPackage_ReturnsNull()
    {
        var service = new NuGetMarketplaceService();
        Assert.IsNull(service.TryResolveInstalled("Not.Installed", "1.0.0", _managedDir));
    }

    [TestMethod]
    public void TryResolveInstalled_NullVersion_ReturnsNull()
    {
        var service = new NuGetMarketplaceService();
        Assert.IsNull(service.TryResolveInstalled("Some.Package", null, _managedDir));
    }

    [TestMethod]
    public async Task InstallLocalFileAsync_PromptsConsent_InstallsToManagedDir_AndMarksLoaded()
    {
        await using var host = new ExtensionHost();
        var consentPrompted = false;
        host.ConsentHandler = (_, _) => { consentPrompted = true; return Task.FromResult(true); };

        var trustPath = Path.Combine(_managedDir, "trust.json");
        var trustStore = ExtensionTrustStore.Load(trustPath);
        var marketplace = new NuGetMarketplaceService();

        var outcome = await MarketplaceLoader.InstallLocalFileAsync(
            host, marketplace, trustStore, SampleDllPath, _managedDir, CancellationToken.None);

        var expectedId = AssemblyName.GetAssemblyName(SampleDllPath).Name!;
        Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
        Assert.IsTrue(consentPrompted, "an untrusted local file must prompt for consent");
        Assert.AreEqual(expectedId, outcome.PackageId);
        Assert.IsTrue(host.IsExtensionPackageLoaded(expectedId));
        Assert.IsTrue(trustStore.IsApproved(expectedId, outcome.ResolvedVersion), "consent should persist trust");
        Assert.IsTrue(Directory.Exists(Path.Combine(_managedDir, expectedId, outcome.ResolvedVersion!)));
    }

    [TestMethod]
    public async Task InstallLocalFileAsync_DeclinedConsent_DoesNotInstall()
    {
        await using var host = new ExtensionHost();
        host.ConsentHandler = (_, _) => Task.FromResult(false);

        var trustStore = ExtensionTrustStore.Load(Path.Combine(_managedDir, "trust.json"));
        var marketplace = new NuGetMarketplaceService();

        var outcome = await MarketplaceLoader.InstallLocalFileAsync(
            host, marketplace, trustStore, SampleDllPath, _managedDir, CancellationToken.None);

        var expectedId = AssemblyName.GetAssemblyName(SampleDllPath).Name!;
        Assert.IsFalse(outcome.Success);
        Assert.IsFalse(host.IsExtensionPackageLoaded(expectedId));
        Assert.IsFalse(trustStore.IsApproved(expectedId, outcome.ResolvedVersion));
    }

    [TestMethod]
    public async Task InstallLocalFileAsync_UnsupportedFile_FailsWithoutPrompting()
    {
        await using var host = new ExtensionHost();
        var consentPrompted = false;
        host.ConsentHandler = (_, _) => { consentPrompted = true; return Task.FromResult(true); };

        var txt = Path.Combine(Path.GetTempPath(), $"verso-{Guid.NewGuid():N}.txt");
        File.WriteAllText(txt, "not an extension");
        try
        {
            var outcome = await MarketplaceLoader.InstallLocalFileAsync(
                host, new NuGetMarketplaceService(),
                ExtensionTrustStore.Load(Path.Combine(_managedDir, "trust.json")),
                txt, _managedDir, CancellationToken.None);

            Assert.IsFalse(outcome.Success);
            Assert.IsFalse(consentPrompted, "an unreadable/unsupported file should fail before prompting");
        }
        finally
        {
            File.Delete(txt);
        }
    }
}
