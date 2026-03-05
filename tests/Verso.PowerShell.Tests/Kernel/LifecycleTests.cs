using Verso.Abstractions;
using Verso.PowerShell.Kernel;
using Verso.Testing.Stubs;

namespace Verso.PowerShell.Tests.Kernel;

[TestClass]
public class ExtensionMetadataTests
{
    [TestMethod]
    public void PowerShellExtension_HasCorrectMetadata()
    {
        var ext = new PowerShell.PowerShellExtension();
        Assert.AreEqual("verso.powershell", ext.ExtensionId);
        Assert.AreEqual("Verso.PowerShell", ext.Name);
        Assert.AreEqual("1.0.0", ext.Version);
        Assert.AreEqual("Datafication", ext.Author);
        Assert.IsNotNull(ext.Description);
    }

    [TestMethod]
    public void PowerShellExtension_ImplementsIExtension()
    {
        Assert.IsTrue(typeof(IExtension).IsAssignableFrom(typeof(PowerShell.PowerShellExtension)));
    }

    [TestMethod]
    public async Task PowerShellExtension_OnLoadedAsync_ReturnsCompleted()
    {
        var ext = new PowerShell.PowerShellExtension();
        await ext.OnLoadedAsync(null!); // Should not throw
    }

    [TestMethod]
    public async Task PowerShellExtension_OnUnloadedAsync_ReturnsCompleted()
    {
        var ext = new PowerShell.PowerShellExtension();
        await ext.OnUnloadedAsync(); // Should not throw
    }
}

[TestClass]
public class LifecycleTests
{
    [TestMethod]
    public void Metadata_HasCorrectValues()
    {
        var kernel = new PowerShellKernel();
        Assert.AreEqual("verso.powershell.kernel", kernel.ExtensionId);
        Assert.AreEqual("PowerShell", kernel.Name);
        Assert.AreEqual("1.0.0", kernel.Version);
        Assert.AreEqual("Datafication", kernel.Author);
        Assert.IsNotNull(kernel.Description);
    }

    [TestMethod]
    public void LanguageProperties_AreCorrect()
    {
        var kernel = new PowerShellKernel();
        Assert.AreEqual("powershell", kernel.LanguageId);
        Assert.AreEqual("PowerShell", kernel.DisplayName);
        Assert.IsTrue(kernel.FileExtensions.Contains(".ps1"));
        Assert.IsTrue(kernel.FileExtensions.Contains(".psm1"));
    }

    [TestMethod]
    public async Task ExecuteBeforeInit_ThrowsInvalidOperationException()
    {
        var kernel = new PowerShellKernel();
        var context = new StubExecutionContext();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        {
            await kernel.ExecuteAsync("1 + 1", context);
        });
    }

    [TestMethod]
    public async Task ExecuteAfterDispose_ThrowsObjectDisposedException()
    {
        var kernel = new PowerShellKernel();
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
        var kernel = new PowerShellKernel();
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
        var kernel = new PowerShellKernel();
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
        var kernel = new PowerShellKernel();
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();
        await kernel.DisposeAsync(); // Should not throw
    }

    [TestMethod]
    public void HasVersoExtensionAttribute()
    {
        var attr = Attribute.GetCustomAttribute(
            typeof(PowerShellKernel), typeof(VersoExtensionAttribute));
        Assert.IsNotNull(attr, "PowerShellKernel should have [VersoExtension] attribute");
    }

    [TestMethod]
    public void ImplementsILanguageKernel()
    {
        Assert.IsTrue(typeof(ILanguageKernel).IsAssignableFrom(typeof(PowerShellKernel)));
    }
}
