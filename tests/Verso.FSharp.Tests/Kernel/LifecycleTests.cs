using Verso.Abstractions;
using Verso.FSharp.Kernel;
using Verso.Testing.Stubs;

namespace Verso.FSharp.Tests.Kernel;

[TestClass]
public class ExtensionMetadataTests
{
    [TestMethod]
    public void FSharpExtension_HasCorrectMetadata()
    {
        var ext = new FSharp.FSharpExtension();
        Assert.AreEqual("verso.fsharp", ext.ExtensionId);
        Assert.AreEqual("Verso.FSharp", ext.Name);
        Assert.AreEqual("0.1.0", ext.Version);
        Assert.AreEqual("Verso Contributors", ext.Author);
        Assert.IsNotNull(ext.Description);
    }

    [TestMethod]
    public void FSharpExtension_ImplementsIExtension()
    {
        Assert.IsTrue(typeof(IExtension).IsAssignableFrom(typeof(FSharp.FSharpExtension)));
    }

    [TestMethod]
    public async Task FSharpExtension_OnLoadedAsync_ReturnsCompleted()
    {
        var ext = new FSharp.FSharpExtension();
        await ext.OnLoadedAsync(null!); // Should not throw
    }

    [TestMethod]
    public async Task FSharpExtension_OnUnloadedAsync_ReturnsCompleted()
    {
        var ext = new FSharp.FSharpExtension();
        await ext.OnUnloadedAsync(); // Should not throw
    }
}

[TestClass]
public class LifecycleTests
{
    [TestMethod]
    public void Metadata_HasCorrectValues()
    {
        var kernel = new FSharpKernel();
        Assert.AreEqual("verso.fsharp.kernel", kernel.ExtensionId);
        Assert.AreEqual("F# (Interactive)", kernel.Name);
        Assert.AreEqual("0.1.0", kernel.Version);
        Assert.AreEqual("Verso Contributors", kernel.Author);
        Assert.IsNotNull(kernel.Description);
    }

    [TestMethod]
    public void LanguageProperties_AreCorrect()
    {
        var kernel = new FSharpKernel();
        Assert.AreEqual("fsharp", kernel.LanguageId);
        Assert.AreEqual("F# (Interactive)", kernel.DisplayName);
        Assert.IsTrue(kernel.FileExtensions.Contains(".fs"));
        Assert.IsTrue(kernel.FileExtensions.Contains(".fsx"));
    }

    [TestMethod]
    public async Task ExecuteBeforeInit_ThrowsInvalidOperationException()
    {
        var kernel = new FSharpKernel();
        var context = new StubExecutionContext();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        {
            await kernel.ExecuteAsync("1 + 1", context);
        });
    }

    [TestMethod]
    public async Task ExecuteAfterDispose_ThrowsObjectDisposedException()
    {
        var kernel = new FSharpKernel();
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();

        var context = new StubExecutionContext();
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(async () =>
        {
            await kernel.ExecuteAsync("1 + 1", context);
        });
    }

    [TestMethod]
    public async Task ReInit_AfterDispose_Works()
    {
        var kernel = new FSharpKernel();
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();

        // Re-initialize
        await kernel.InitializeAsync();
        var context = new StubExecutionContext();
        var outputs = await kernel.ExecuteAsync("1 + 1", context);
        Assert.IsTrue(outputs.Count > 0);
        Assert.IsFalse(outputs.Any(o => o.IsError));

        await kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task DoubleInit_IsIdempotent()
    {
        var kernel = new FSharpKernel();
        await kernel.InitializeAsync();
        await kernel.InitializeAsync(); // Should not throw

        var context = new StubExecutionContext();
        var outputs = await kernel.ExecuteAsync("42", context);
        Assert.IsTrue(outputs.Count > 0);

        await kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task DoubleDispose_IsIdempotent()
    {
        var kernel = new FSharpKernel();
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();
        await kernel.DisposeAsync(); // Should not throw
    }

    [TestMethod]
    public void HasVersoExtensionAttribute()
    {
        var attr = Attribute.GetCustomAttribute(
            typeof(FSharpKernel), typeof(VersoExtensionAttribute));
        Assert.IsNotNull(attr, "FSharpKernel should have [VersoExtension] attribute");
    }

    [TestMethod]
    public void ImplementsILanguageKernel()
    {
        Assert.IsTrue(typeof(ILanguageKernel).IsAssignableFrom(typeof(FSharpKernel)));
    }
}
