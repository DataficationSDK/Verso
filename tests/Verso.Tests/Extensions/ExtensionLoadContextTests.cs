using System.Runtime.Loader;
using Verso.Extensions;

namespace Verso.Tests.Extensions;

[TestClass]
public class ExtensionLoadContextTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"verso-alc-test-{Guid.NewGuid():N}");
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

    // --- ALC name ---

    [TestMethod]
    public void Constructor_SetsNameFromAssemblyFileName()
    {
        var context = new ExtensionLoadContext("/fake/path/MyExtension.dll");

        Assert.AreEqual("VersoExt:MyExtension", context.Name);

        context.Unload();
    }

    // --- Collectibility ---

    [TestMethod]
    public void Constructor_IsCollectible()
    {
        var context = new ExtensionLoadContext("/fake/path/Test.dll");

        Assert.IsTrue(context.IsCollectible);

        context.Unload();
    }

    // --- Verso.Abstractions returns host assembly directly ---

    [TestMethod]
    public void Load_VersoAbstractions_ReturnsHostAssembly()
    {
        var context = new ExtensionLoadContext("/fake/path/Plugin.dll");

        // ExtensionLoadContext returns the host's Verso.Abstractions assembly directly
        // to preserve type identity, even when the extension was compiled against a
        // different version.
        var abstractionsAssembly = typeof(Verso.Abstractions.IExtension).Assembly;
        var assemblyName = abstractionsAssembly.GetName();

        // Use reflection to call the protected Load method
        var loadMethod = typeof(AssemblyLoadContext).GetMethod(
            "Load",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            new[] { typeof(System.Reflection.AssemblyName) });

        var result = loadMethod?.Invoke(context, new object[] { assemblyName });

        Assert.AreSame(abstractionsAssembly, result,
            "Verso.Abstractions should return the host assembly to preserve type identity.");

        context.Unload();
    }

    // --- Satellite resource assemblies ---

    [TestMethod]
    public void Load_SatelliteBesideTheExtension_ResolvesTranslatedStrings()
    {
        // An extension that ships translations the standard way. The isolated load context has
        // to find the satellite for the extension's ResourceManager, or every extension answers
        // in English. The runtime probes the culture folder beside the assembly for any load
        // context, so no probing of our own is needed; this proves that stays true.
        var mainPath = SatelliteProbe.Build(_dir);

        var context = new ExtensionLoadContext(mainPath);
        try
        {
            var assembly = context.LoadFromAssemblyPath(mainPath);

            Assert.AreEqual("Hallo", SatelliteProbe.Greeting(assembly, "de"),
                "The satellite beside the extension was not found from its load context.");
            Assert.AreEqual("Hallo", SatelliteProbe.Greeting(assembly, "de-AT"),
                "A regional culture falls back to its parent's satellite.");
            Assert.AreEqual("Hello", SatelliteProbe.Greeting(assembly, "fr"),
                "A language with no satellite falls back to the neutral resource.");
        }
        finally
        {
            context.Unload();
        }
    }
}
