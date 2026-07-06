using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Verso.Extensions;
using Verso.Extensions.Marketplace;

namespace Verso.Tests.Extensions;

/// <summary>
/// Covers how <see cref="MarketplaceLoader.LoadAssembliesAsync"/> treats assemblies that
/// cannot register extensions: harmless non-extension assemblies are skipped silently,
/// but an assembly rejected for referencing a newer Verso.Abstractions must propagate,
/// so an install never reports success while loading nothing.
/// </summary>
[TestClass]
public class MarketplaceLoaderCompatibilityTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"verso-compat-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch { /* best effort */ }
    }

    [TestMethod]
    public async Task LoadAssembliesAsync_NewerAbstractionsReference_PropagatesIncompatibility()
    {
        // An extension compiled against a newer minor of Verso.Abstractions than this
        // host: stand in for a package published by a future release.
        var newerAbstractions = Compile(
            "Verso.Abstractions",
            """
            [assembly: System.Reflection.AssemblyVersion("1.99.0.0")]
            namespace Verso.Abstractions { public class Marker { } }
            """);
        var extensionDll = Compile(
            "Verso.Compat.Probe",
            "public class Probe : Verso.Abstractions.Marker { }",
            newerAbstractions);

        await using var host = new ExtensionHost();

        var ex = await Assert.ThrowsExceptionAsync<ExtensionLoadException>(
            () => MarketplaceLoader.LoadAssembliesAsync(host, new[] { extensionDll }));

        Assert.IsTrue(ex.Errors.Any(e => e.ErrorCode == "INCOMPATIBLE_VERSION"));
    }

    [TestMethod]
    public async Task LoadAssembliesAsync_CompatibleNonExtensionAssembly_SkipsSilently()
    {
        // References the host's own Verso.Abstractions but declares no extension types,
        // like a dependency assembly shipped alongside an extension package.
        var dependencyDll = Compile(
            "Verso.Compat.Plain",
            "public class Plain { public Verso.Abstractions.IExtension? Extension; }",
            typeof(Verso.Abstractions.IExtension).Assembly.Location);

        await using var host = new ExtensionHost();

        var registered = await MarketplaceLoader.LoadAssembliesAsync(host, new[] { dependencyDll });

        Assert.AreEqual(0, registered);
        Assert.AreEqual(0, host.GetLoadedExtensions().Count);
    }

    /// <summary>Compiles a small assembly to disk and returns its path.</summary>
    private string Compile(string assemblyName, string source, params string[] referencePaths)
    {
        var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = tpa.Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "System.Private.CoreLib.dll")
            .Concat(referencePaths)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(_dir, $"{assemblyName}.dll");
        var result = compilation.Emit(path);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }
}
