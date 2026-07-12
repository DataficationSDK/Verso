using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
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
    public async Task InstallFromFileAsync_Dll_CopiesIntoRuntimePartitionedVersionDir()
    {
        var service = new NuGetMarketplaceService();

        var result = await service.InstallFromFileAsync(SampleDllPath, _managedDir, CancellationToken.None);

        var expectedName = AssemblyName.GetAssemblyName(SampleDllPath).Name!;
        Assert.AreEqual(expectedName, result.PackageId);
        // Installs are partitioned by the runtime the assemblies require so hosts running
        // on different .NET majors can share the managed directory.
        Assert.AreEqual(
            Path.Combine(_managedDir, result.PackageId, result.ResolvedVersion),
            Path.GetDirectoryName(result.PackageDirectory));
        StringAssert.Matches(Path.GetFileName(result.PackageDirectory), new Regex(@"^net\d+\.0$"));
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

    [TestMethod]
    public async Task EnsureInstalled_TraversalVersion_ThrowsBeforeTouchingDisk()
    {
        var service = new NuGetMarketplaceService();

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            service.EnsureInstalledAsync("Pkg", "../../escape", _managedDir, CancellationToken.None));

        // The guard must reject the segment before any directory work, so nothing escapes.
        var escaped = Path.GetFullPath(Path.Combine(_managedDir, "Pkg", "../../escape"));
        Assert.IsFalse(Directory.Exists(escaped));
    }

    [TestMethod]
    public async Task EnsureInstalled_TraversalPackageId_Throws()
    {
        var service = new NuGetMarketplaceService();

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            service.EnsureInstalledAsync("../../evil", "1.0.0", _managedDir, CancellationToken.None));
    }

    [TestMethod]
    public void TryResolveInstalled_TraversalSegment_ReturnsNull()
    {
        var service = new NuGetMarketplaceService();

        Assert.IsNull(service.TryResolveInstalled("Pkg", "../../escape", _managedDir));
        Assert.IsNull(service.TryResolveInstalled("../../evil", "1.0.0", _managedDir));
    }

    [TestMethod]
    public void TryResolveInstalled_PicksNewestRuntimeFolderThisRuntimeCanLoad()
    {
        var current = Environment.Version.Major;
        var versionDir = Path.Combine(_managedDir, "Pkg", "1.0.0");
        foreach (var label in new[] { "net6.0", $"net{current}.0", $"net{current + 1}.0" })
        {
            var dir = Path.Combine(versionDir, label);
            Directory.CreateDirectory(dir);
            File.Copy(SampleDllPath, Path.Combine(dir, "Pkg.dll"));
        }

        var service = new NuGetMarketplaceService();
        var resolved = service.TryResolveInstalled("Pkg", "1.0.0", _managedDir);

        Assert.IsNotNull(resolved);
        Assert.AreEqual(Path.Combine(versionDir, $"net{current}.0"), resolved.PackageDirectory);
    }

    [TestMethod]
    public void TryResolveInstalled_OnlyNewerRuntimeCopy_ReturnsNull()
    {
        // A copy written by a host on a newer runtime must not resolve: loading it would
        // fail with a type-load error long after the install claimed to succeed.
        var newer = Path.Combine(_managedDir, "Pkg", "1.0.0", $"net{Environment.Version.Major + 2}.0");
        Directory.CreateDirectory(newer);
        File.Copy(SampleDllPath, Path.Combine(newer, "Pkg.dll"));

        var service = new NuGetMarketplaceService();

        Assert.IsNull(service.TryResolveInstalled("Pkg", "1.0.0", _managedDir));
    }

    [TestMethod]
    public void TryResolveInstalled_LegacyFlatCompatibleCopy_Resolves()
    {
        // Installs made before runtime partitioning put assemblies directly in the
        // version folder; a copy this runtime can load must keep resolving.
        var versionDir = Path.Combine(_managedDir, "Pkg", "1.0.0");
        Directory.CreateDirectory(versionDir);
        File.Copy(SampleDllPath, Path.Combine(versionDir, "Pkg.dll"));

        var service = new NuGetMarketplaceService();
        var resolved = service.TryResolveInstalled("Pkg", "1.0.0", _managedDir);

        Assert.IsNotNull(resolved);
        Assert.AreEqual(versionDir, resolved.PackageDirectory);
    }

    [TestMethod]
    public void TryResolveInstalled_LegacyFlatCopyRequiringNewerRuntime_ReturnsNull()
    {
        var versionDir = Path.Combine(_managedDir, "Pkg", "1.0.0");
        Directory.CreateDirectory(versionDir);
        WriteAssemblyRequiringRuntime(Environment.Version.Major + 2, Path.Combine(versionDir, "Pkg.dll"));

        var service = new NuGetMarketplaceService();

        Assert.IsNull(service.TryResolveInstalled("Pkg", "1.0.0", _managedDir));
    }

    [TestMethod]
    public void GetRequiredRuntimeMajor_ReadsSystemRuntimeReference()
    {
        Directory.CreateDirectory(_managedDir);
        var fake = Path.Combine(_managedDir, "fake.dll");
        WriteAssemblyRequiringRuntime(Environment.Version.Major + 2, fake);

        Assert.AreEqual(Environment.Version.Major + 2, NuGetMarketplaceService.GetRequiredRuntimeMajor(fake));
        Assert.AreEqual(
            Environment.Version.Major,
            NuGetMarketplaceService.GetRequiredRuntimeMajor(SampleDllPath),
            "the test's own Verso build should require exactly the runtime the tests run on");
    }

    [TestMethod]
    public async Task EnsureInstalled_ReusesCompatibleInstalledCopy()
    {
        var dir = Path.Combine(_managedDir, "Pkg", "1.0.0", $"net{Environment.Version.Major}.0");
        Directory.CreateDirectory(dir);
        File.Copy(SampleDllPath, Path.Combine(dir, "Pkg.dll"));

        var service = new NuGetMarketplaceService();
        var result = await service.EnsureInstalledAsync("Pkg", "1.0.0", _managedDir, CancellationToken.None);

        Assert.AreEqual("1.0.0", result.ResolvedVersion);
        Assert.AreEqual(dir, result.PackageDirectory);
    }

    [TestMethod]
    public async Task EnsureInstalled_LegacyFlatCompatibleCopy_ReusedWithoutReinstall()
    {
        var versionDir = Path.Combine(_managedDir, "Pkg", "1.0.0");
        Directory.CreateDirectory(versionDir);
        File.Copy(SampleDllPath, Path.Combine(versionDir, "Pkg.dll"));

        var service = new NuGetMarketplaceService();
        var result = await service.EnsureInstalledAsync("Pkg", "1.0.0", _managedDir, CancellationToken.None);

        Assert.AreEqual(versionDir, result.PackageDirectory);
    }

    /// <summary>
    /// Writes a minimal managed assembly whose System.Runtime reference demands the given
    /// runtime major, without needing that runtime installed. Lets the compatibility probe
    /// be exercised against assemblies newer than the running host.
    /// </summary>
    private static void WriteAssemblyRequiringRuntime(int runtimeMajor, string path)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0, metadata.GetOrAddString(Path.GetFileName(path)),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(
            metadata.GetOrAddString(Path.GetFileNameWithoutExtension(path)),
            new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(runtimeMajor, 0, 0, 0), default, default, 0, default);
        metadata.AddTypeDefinition(
            default, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }
}
