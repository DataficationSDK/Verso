using Verso.Abstractions;
using Verso.Extensions;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class BuiltInExtensionDiscoveryTests
{
    [TestMethod]
    public async Task LoadBuiltInExtensions_Discovers20Extensions()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        var all = host.GetLoadedExtensions();
        Assert.AreEqual(24, all.Count, $"Expected 24 extensions, got {all.Count}: {string.Join(", ", all.Select(e => e.ExtensionId))}");

    }

    [TestMethod]
    public async Task LoadBuiltInExtensions_AllIdsUnique()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        var all = host.GetLoadedExtensions();
        var ids = all.Select(e => e.ExtensionId).ToList();
        Assert.AreEqual(ids.Count, ids.Distinct().Count(), "Extension IDs are not all unique");
    }

    [TestMethod]
    public async Task LoadBuiltInExtensions_AllIdsHaveVersoPrefix()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        var all = host.GetLoadedExtensions();
        foreach (var ext in all)
        {
            Assert.IsTrue(ext.ExtensionId.StartsWith("verso."),
                $"Extension '{ext.ExtensionId}' does not start with 'verso.'");
        }
    }

    [TestMethod]
    public async Task LoadBuiltInExtensions_Has1Kernel()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        Assert.AreEqual(1, host.GetKernels().Count);
    }

    [TestMethod]
    public async Task LoadBuiltInExtensions_Has1Renderer()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        Assert.AreEqual(1, host.GetRenderers().Count);
    }

    [TestMethod]
    public async Task LoadBuiltInExtensions_Has5Formatters()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        Assert.AreEqual(6, host.GetFormatters().Count);
    }

    [TestMethod]
    public async Task LoadBuiltInExtensions_Has2Themes()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        Assert.AreEqual(2, host.GetThemes().Count);
    }

    [TestMethod]
    public async Task LoadBuiltInExtensions_Has1Layout()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        Assert.AreEqual(2, host.GetLayouts().Count);
    }
}
