using Verso.Abstractions;
using Verso.Stubs;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Stubs;

[TestClass]
public sealed class StubExtensionHostContextTests
{
    [TestMethod]
    public void GetKernels_Reflects_Registered_Kernels()
    {
        var kernel = new FakeLanguageKernel("csharp", "C#");
        var kernels = new List<ILanguageKernel> { kernel };
        var stub = new StubExtensionHostContext(() => kernels);

        var result = stub.GetKernels();
        Assert.AreEqual(1, result.Count);
        Assert.AreSame(kernel, result[0]);
    }

    [TestMethod]
    public void GetKernels_Returns_Empty_When_No_Kernels()
    {
        var stub = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
        Assert.AreEqual(0, stub.GetKernels().Count);
    }

    [TestMethod]
    public void GetLoadedExtensions_Returns_Kernels()
    {
        var kernel = new FakeLanguageKernel();
        var stub = new StubExtensionHostContext(() => new[] { kernel });

        var extensions = stub.GetLoadedExtensions();
        Assert.AreEqual(1, extensions.Count);
        Assert.AreSame(kernel, extensions[0]);
    }

    [TestMethod]
    public void GetRenderers_Returns_Empty()
    {
        var stub = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
        Assert.AreEqual(0, stub.GetRenderers().Count);
    }

    [TestMethod]
    public void GetFormatters_Returns_Empty()
    {
        var stub = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
        Assert.AreEqual(0, stub.GetFormatters().Count);
    }

    [TestMethod]
    public void GetCellTypes_Returns_Empty()
    {
        var stub = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
        Assert.AreEqual(0, stub.GetCellTypes().Count);
    }

    [TestMethod]
    public void GetSerializers_Returns_Empty()
    {
        var stub = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
        Assert.AreEqual(0, stub.GetSerializers().Count);
    }

    [TestMethod]
    public void Reflects_Dynamic_Kernel_Changes()
    {
        var kernels = new List<ILanguageKernel>();
        var stub = new StubExtensionHostContext(() => kernels.ToList());

        Assert.AreEqual(0, stub.GetKernels().Count);

        kernels.Add(new FakeLanguageKernel("python", "Python"));
        Assert.AreEqual(1, stub.GetKernels().Count);
    }
}
