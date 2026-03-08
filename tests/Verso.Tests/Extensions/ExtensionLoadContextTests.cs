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
}
