using System.Runtime.Loader;
using Verso.Extensions;

namespace Verso.Tests.Extensions;

[TestClass]
public class ExtensionLoadContextTests
{
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

    // --- Verso.Abstractions passthrough to default context ---

    [TestMethod]
    public void Load_VersoAbstractions_FallsBackToDefaultContext()
    {
        var context = new ExtensionLoadContext("/fake/path/Plugin.dll");

        // When Load returns null, the runtime falls back to the default ALC.
        // We verify that requesting the Verso.Abstractions assembly name returns null
        // from the custom context (meaning it defers to default).
        var abstractionsAssembly = typeof(Verso.Abstractions.IExtension).Assembly;
        var assemblyName = abstractionsAssembly.GetName();

        // Use reflection to call the protected Load method
        var loadMethod = typeof(AssemblyLoadContext).GetMethod(
            "Load",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            new[] { typeof(System.Reflection.AssemblyName) });

        var result = loadMethod?.Invoke(context, new object[] { assemblyName });

        Assert.IsNull(result, "Verso.Abstractions should return null to fall back to the default context.");

        context.Unload();
    }
}
