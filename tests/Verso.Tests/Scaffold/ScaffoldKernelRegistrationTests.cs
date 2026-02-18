using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Scaffold;

[TestClass]
public sealed class ScaffoldKernelRegistrationTests
{
    private Verso.Scaffold _scaffold = null!;

    [TestInitialize]
    public void Setup() => _scaffold = new Verso.Scaffold();

    [TestMethod]
    public void RegisterKernel_AddsToRegistry()
    {
        var kernel = new FakeLanguageKernel("csharp", "C#");
        _scaffold.RegisterKernel(kernel);

        Assert.AreSame(kernel, _scaffold.GetKernel("csharp"));
    }

    [TestMethod]
    public void RegisterKernel_AppearsInRegisteredLanguages()
    {
        _scaffold.RegisterKernel(new FakeLanguageKernel("csharp"));
        _scaffold.RegisterKernel(new FakeLanguageKernel("python"));

        var languages = _scaffold.RegisteredLanguages;
        Assert.AreEqual(2, languages.Count);
        Assert.IsTrue(languages.Contains("csharp"));
        Assert.IsTrue(languages.Contains("python"));
    }

    [TestMethod]
    public void RegisterKernel_Duplicate_Throws()
    {
        _scaffold.RegisterKernel(new FakeLanguageKernel("csharp"));
        Assert.ThrowsException<InvalidOperationException>(
            () => _scaffold.RegisterKernel(new FakeLanguageKernel("csharp")));
    }

    [TestMethod]
    public void RegisterKernel_Null_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(
            () => _scaffold.RegisterKernel(null!));
    }

    [TestMethod]
    public void UnregisterKernel_Existing_ReturnsTrue()
    {
        _scaffold.RegisterKernel(new FakeLanguageKernel("csharp"));
        Assert.IsTrue(_scaffold.UnregisterKernel("csharp"));
        Assert.IsNull(_scaffold.GetKernel("csharp"));
    }

    [TestMethod]
    public void UnregisterKernel_NonExistent_ReturnsFalse()
    {
        Assert.IsFalse(_scaffold.UnregisterKernel("missing"));
    }

    [TestMethod]
    public void GetKernel_CaseInsensitive()
    {
        var kernel = new FakeLanguageKernel("CSharp");
        _scaffold.RegisterKernel(kernel);

        Assert.AreSame(kernel, _scaffold.GetKernel("csharp"));
        Assert.AreSame(kernel, _scaffold.GetKernel("CSHARP"));
    }

    [TestMethod]
    public void GetKernel_NonExistent_ReturnsNull()
    {
        Assert.IsNull(_scaffold.GetKernel("missing"));
    }

    [TestMethod]
    public void GetKernel_Null_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => _scaffold.GetKernel(null!));
    }

    [TestMethod]
    public void RegisterKernel_DuplicateCaseInsensitive_Throws()
    {
        _scaffold.RegisterKernel(new FakeLanguageKernel("CSharp"));
        Assert.ThrowsException<InvalidOperationException>(
            () => _scaffold.RegisterKernel(new FakeLanguageKernel("csharp")));
    }

    [TestMethod]
    public async Task DisposeAsync_DisposesAllKernels()
    {
        var k1 = new FakeLanguageKernel("a");
        var k2 = new FakeLanguageKernel("b");
        _scaffold.RegisterKernel(k1);
        _scaffold.RegisterKernel(k2);

        await _scaffold.DisposeAsync();

        Assert.AreEqual(1, k1.DisposeCallCount);
        Assert.AreEqual(1, k2.DisposeCallCount);
    }

    [TestMethod]
    public void ExtensionHostContext_ReflectsKernels()
    {
        _scaffold.RegisterKernel(new FakeLanguageKernel("csharp"));
        var kernels = _scaffold.ExtensionHostContext.GetKernels();
        Assert.AreEqual(1, kernels.Count);
    }
}
