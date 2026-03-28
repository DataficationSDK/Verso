using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Extensions;

namespace Verso.Cli.Tests.Integration;

[TestClass]
public class InfoCommandIntegrationTests
{
    [TestMethod]
    public async Task LoadBuiltInExtensions_DiscoversCSharpKernel()
    {
        var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        var kernels = host.GetKernels();
        Assert.IsTrue(kernels.Count > 0, "Should discover at least one kernel.");
        Assert.IsTrue(kernels.Any(k => k.LanguageId == "csharp"), "Should discover C# kernel.");

        await host.DisposeAsync();
    }

    [TestMethod]
    public async Task LoadBuiltInExtensions_DiscoversSerializers()
    {
        var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        var serializers = host.GetSerializers();
        Assert.IsTrue(serializers.Count >= 3, "Should discover at least .verso, .ipynb, and .dib serializers.");

        await host.DisposeAsync();
    }

    [TestMethod]
    public async Task LoadBuiltInExtensions_VersionsAreNotEmpty()
    {
        var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        foreach (var kernel in host.GetKernels())
        {
            Assert.IsFalse(string.IsNullOrEmpty(kernel.Version),
                $"Kernel {kernel.ExtensionId} should have a version.");
        }

        await host.DisposeAsync();
    }
}
