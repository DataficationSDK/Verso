using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Utilities;
using Verso.Extensions;

namespace Verso.Cli.Tests.Utilities;

[TestClass]
public class SerializerResolverTests
{
    private ExtensionHost _host = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _host = new ExtensionHost();
        await _host.LoadBuiltInExtensionsAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _host.DisposeAsync();
    }

    [TestMethod]
    public void Resolve_VersoExtension_ReturnsSerializer()
    {
        var serializer = SerializerResolver.Resolve(_host, "notebook.verso");
        Assert.IsNotNull(serializer);
        Assert.IsTrue(serializer.FileExtensions.Contains(".verso"));
    }

    [TestMethod]
    public void Resolve_IpynbExtension_ReturnsSerializer()
    {
        var serializer = SerializerResolver.Resolve(_host, "notebook.ipynb");
        Assert.IsNotNull(serializer);
        Assert.IsTrue(serializer.FileExtensions.Contains(".ipynb"));
    }

    [TestMethod]
    public void Resolve_DibExtension_ReturnsSerializer()
    {
        var serializer = SerializerResolver.Resolve(_host, "notebook.dib");
        Assert.IsNotNull(serializer);
        Assert.IsTrue(serializer.FileExtensions.Contains(".dib"));
    }

    [TestMethod]
    public void Resolve_UnknownExtension_ThrowsSerializerNotFoundException()
    {
        Assert.ThrowsException<SerializerNotFoundException>(
            () => SerializerResolver.Resolve(_host, "document.txt"));
    }

    [TestMethod]
    public void Resolve_UnknownExtension_ErrorMessageContainsSupportedFormats()
    {
        try
        {
            SerializerResolver.Resolve(_host, "document.xyz");
            Assert.Fail("Expected SerializerNotFoundException.");
        }
        catch (SerializerNotFoundException ex)
        {
            Assert.IsTrue(ex.Message.Contains(".verso"), "Error should mention .verso");
            Assert.IsTrue(ex.Message.Contains(".ipynb"), "Error should mention .ipynb");
            Assert.IsTrue(ex.Message.Contains(".dib"), "Error should mention .dib");
        }
    }

    [TestMethod]
    public void ResolveByFormat_Verso_ReturnsSerializer()
    {
        var serializer = SerializerResolver.ResolveByFormat(_host, "verso");
        Assert.IsNotNull(serializer);
        Assert.AreEqual("verso", serializer.FormatId);
    }

    [TestMethod]
    public void ResolveByFormat_Ipynb_ReturnsJupyterSerializer()
    {
        var serializer = SerializerResolver.ResolveByFormat(_host, "ipynb");
        Assert.IsNotNull(serializer);
        Assert.AreEqual("jupyter", serializer.FormatId);
    }

    [TestMethod]
    public void ResolveByFormat_Dib_ReturnsSerializer()
    {
        var serializer = SerializerResolver.ResolveByFormat(_host, "dib");
        Assert.IsNotNull(serializer);
        Assert.AreEqual("dib", serializer.FormatId);
    }

    [TestMethod]
    public void ResolveByFormat_CaseInsensitive()
    {
        var serializer = SerializerResolver.ResolveByFormat(_host, "VERSO");
        Assert.IsNotNull(serializer);
        Assert.AreEqual("verso", serializer.FormatId);
    }

    [TestMethod]
    public void ResolveByFormat_UnknownFormat_ThrowsSerializerNotFoundException()
    {
        Assert.ThrowsException<SerializerNotFoundException>(
            () => SerializerResolver.ResolveByFormat(_host, "pdf"));
    }

    [TestMethod]
    public void ResolveByFormat_UnknownFormat_ErrorMessageContainsSupportedFormats()
    {
        try
        {
            SerializerResolver.ResolveByFormat(_host, "pdf");
            Assert.Fail("Expected SerializerNotFoundException.");
        }
        catch (SerializerNotFoundException ex)
        {
            Assert.IsTrue(ex.Message.Contains("verso"), "Error should mention verso");
            Assert.IsTrue(ex.Message.Contains("ipynb"), "Error should mention ipynb");
            Assert.IsTrue(ex.Message.Contains("dib"), "Error should mention dib");
        }
    }
}
