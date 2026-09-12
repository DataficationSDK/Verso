using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Verso.Extensions;
using Verso.Extensions.Marketplace;
using Verso.Kernels;

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
    public async Task LoadRequired_PreSessionPass_LoadsApprovedLocalCopyOfUnpinnedReference()
    {
        // Lay a copy in the managed store the way a prior install would have.
        var service = new NuGetMarketplaceService();
        var installed = await service.InstallFromFileAsync(SampleDllPath, _managedDir, CancellationToken.None);

        await using var host = new ExtensionHost();
        var trustStore = ExtensionTrustStore.Load(Path.Combine(_managedDir, "trust.json"));
        trustStore.Approve(installed.PackageId, installed.ResolvedVersion);

        var notebook = new NotebookModel();
        notebook.RequiredExtensions.Add(installed.PackageId); // unpinned reference

        // The pre-session pass has no consent channel and must stay offline, but an
        // approved copy already on disk should load so the extension's layouts are
        // registered before the notebook resolves its active layout.
        await MarketplaceLoader.LoadRequiredAsync(
            host, notebook, trustStore, service, _managedDir, promptForConsent: false);

        Assert.IsTrue(host.IsExtensionPackageLoaded(installed.PackageId));
    }

    [TestMethod]
    public async Task LoadRequired_PreSessionPass_SkipsUnapprovedLocalCopy()
    {
        var service = new NuGetMarketplaceService();
        var installed = await service.InstallFromFileAsync(SampleDllPath, _managedDir, CancellationToken.None);

        await using var host = new ExtensionHost();
        var trustStore = ExtensionTrustStore.Load(Path.Combine(_managedDir, "trust.json"));

        var notebook = new NotebookModel();
        notebook.RequiredExtensions.Add(installed.PackageId);

        // Present on disk but never approved: the pre-session pass must not load it
        // (consent belongs to the later pass, which has the client channel).
        await MarketplaceLoader.LoadRequiredAsync(
            host, notebook, trustStore, service, _managedDir, promptForConsent: false);

        Assert.IsFalse(host.IsExtensionPackageLoaded(installed.PackageId));
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

    [TestMethod]
    public void CopySatellitesToManaged_MirrorsCultureFoldersAndNothingElse()
    {
        var cacheRoot = Path.Combine(_managedDir, "cache");
        var packageDir = Path.Combine(cacheRoot, "Pkg", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packageDir, "de"));
        Directory.CreateDirectory(Path.Combine(packageDir, "native"));
        Directory.CreateDirectory(Path.Combine(packageDir, "notes"));
        File.WriteAllText(Path.Combine(packageDir, "Pkg.dll"), "main");
        File.WriteAllText(Path.Combine(packageDir, "de", "Pkg.resources.dll"), "de");
        File.WriteAllText(Path.Combine(packageDir, "native", "libx.dylib"), "native");
        File.WriteAllText(Path.Combine(packageDir, "notes", "readme.txt"), "text");

        var target = Path.Combine(_managedDir, "target");
        NuGetMarketplaceService.CopySatellitesToManaged(new[] { ("Pkg", "1.0.0") }, target, cacheRoot);

        Assert.IsTrue(File.Exists(Path.Combine(target, "de", "Pkg.resources.dll")));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "native")), "Natives have their own copy step.");
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "notes")), "A folder with no satellites is not a culture.");
        Assert.IsFalse(File.Exists(Path.Combine(target, "Pkg.dll")), "Assemblies have their own copy step.");
    }

    [TestMethod]
    public async Task InstallFromFileAsync_Nupkg_KeepsSatellitesUnderTheirCultureFolder()
    {
        // A package laid out the way dotnet pack lays out a localized extension. Installing it
        // must keep de/Probe.resources.dll under its culture folder, list only the real assembly,
        // and leave the installed copy able to answer in German from its own load context.
        var build = Path.Combine(_managedDir, "build");
        Directory.CreateDirectory(build);
        var mainDll = SatelliteProbe.Build(build);
        var id = "Verso.SatelliteProbe." + Guid.NewGuid().ToString("N")[..12];
        var nupkg = BuildNupkg(build, id, "1.0.0", mainDll, SatelliteProbe.SatellitePath(build));

        try
        {
            var installed = await new NuGetMarketplaceService()
                .InstallFromFileAsync(nupkg, _managedDir, CancellationToken.None);

            var installedDll = Path.Combine(installed.PackageDirectory, "Probe.dll");
            Assert.IsTrue(File.Exists(Path.Combine(installed.PackageDirectory, "de", "Probe.resources.dll")),
                "The satellite lost its culture folder on the way into the managed store.");
            CollectionAssert.AreEquivalent(
                new[] { "Probe.dll" },
                Directory.GetFiles(installed.PackageDirectory, "*.dll").Select(Path.GetFileName).ToArray(),
                "Only the real assembly belongs at the top level.");
            CollectionAssert.AreEquivalent(new[] { installedDll }, installed.AssemblyPaths.ToArray());

            var context = new ExtensionLoadContext(installedDll);
            try
            {
                var assembly = context.LoadFromAssemblyPath(installedDll);
                Assert.AreEqual("Hallo", SatelliteProbe.Greeting(assembly, "de"));
                Assert.AreEqual("Hello", SatelliteProbe.Greeting(assembly, "fr"));
            }
            finally
            {
                context.Unload();
            }
        }
        finally
        {
            // The resolver cached the package under its real cache root; leave nothing behind.
            try { Directory.Delete(Path.Combine(NuGetPackageResolver.CacheRoot, id), recursive: true); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public async Task InstallFromFileAsync_Nupkg_RefreshesACacheEntryWithFlattenedSatellites()
    {
        // A cache entry written by a build that flattened lib/ has the satellite beside the
        // assembly, where it can never load. The next install must extract the package again,
        // put the satellite back under its culture folder and drop the flattened copy, so the
        // entry is not refreshed again on every resolve after that.
        var build = Path.Combine(_managedDir, "build");
        Directory.CreateDirectory(build);
        var mainDll = SatelliteProbe.Build(build);
        var id = "Verso.SatelliteProbe." + Guid.NewGuid().ToString("N")[..12];
        var nupkg = BuildNupkg(build, id, "1.0.0", mainDll, SatelliteProbe.SatellitePath(build));
        var cacheDir = Path.Combine(NuGetPackageResolver.CacheRoot, id, "1.0.0");

        try
        {
            var service = new NuGetMarketplaceService();
            await service.InstallFromFileAsync(nupkg, Path.Combine(_managedDir, "first"), CancellationToken.None);

            // Age the entry by hand into the shape the older build left behind.
            var cultureDir = Path.Combine(cacheDir, "de");
            File.Move(Path.Combine(cultureDir, "Probe.resources.dll"), Path.Combine(cacheDir, "Probe.resources.dll"));
            Directory.Delete(cultureDir);

            var installed = await service.InstallFromFileAsync(nupkg, Path.Combine(_managedDir, "second"), CancellationToken.None);

            Assert.IsTrue(File.Exists(Path.Combine(cacheDir, "de", "Probe.resources.dll")),
                "The stale entry was served from cache instead of being extracted again.");
            Assert.IsFalse(File.Exists(Path.Combine(cacheDir, "Probe.resources.dll")),
                "The flattened copy was left behind, so the entry would be refreshed on every resolve.");
            Assert.IsTrue(File.Exists(Path.Combine(installed.PackageDirectory, "de", "Probe.resources.dll")),
                "The refreshed install did not receive the satellite under its culture folder.");
            CollectionAssert.AreEquivalent(
                new[] { "Probe.dll" },
                Directory.GetFiles(installed.PackageDirectory, "*.dll").Select(Path.GetFileName).ToArray(),
                "Only the real assembly belongs at the top level.");
        }
        finally
        {
            try { Directory.Delete(Path.Combine(NuGetPackageResolver.CacheRoot, id), recursive: true); } catch { /* best effort */ }
        }
    }

    private static string BuildNupkg(string folder, string id, string version, string mainDll, string satelliteDll)
    {
        var path = Path.Combine(folder, $"{id}.{version}.nupkg");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var nuspec = new StreamWriter(zip.CreateEntry($"{id}.nuspec").Open()))
        {
            nuspec.Write(
                $"""<?xml version="1.0" encoding="utf-8"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{id}</id><version>{version}</version><authors>test</authors><description>Satellite probe.</description></metadata></package>""");
        }
        zip.CreateEntryFromFile(mainDll, "lib/net8.0/Probe.dll");
        zip.CreateEntryFromFile(satelliteDll, "lib/net8.0/de/Probe.resources.dll");
        return path;
    }
}
