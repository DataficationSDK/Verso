using Verso.Abstractions;
using Verso.Python.Kernel;
using Verso.Testing.Stubs;

namespace Verso.Python.Tests.Kernel;

[TestClass]
public sealed class LifecycleTests
{
    // ---- Unit tests (no Python runtime needed) ----

    [TestMethod]
    public void Metadata_HasCorrectValues()
    {
        var kernel = new PythonKernel();
        Assert.AreEqual("verso.kernel.python", kernel.ExtensionId);
        Assert.AreEqual("Python (pythonnet)", kernel.Name);
        Assert.AreEqual("1.0.0", kernel.Version);
        Assert.AreEqual("Verso Contributors", kernel.Author);
        Assert.IsNotNull(kernel.Description);
    }

    [TestMethod]
    public void LanguageProperties_AreCorrect()
    {
        var kernel = new PythonKernel();
        Assert.AreEqual("python", kernel.LanguageId);
        Assert.AreEqual("Python", kernel.DisplayName);
        Assert.IsTrue(kernel.FileExtensions.Contains(".py"));
    }

    [TestMethod]
    public void HasVersoExtensionAttribute()
    {
        var attr = Attribute.GetCustomAttribute(
            typeof(PythonKernel), typeof(VersoExtensionAttribute));
        Assert.IsNotNull(attr, "PythonKernel should have [VersoExtension] attribute");
    }

    [TestMethod]
    public void ImplementsILanguageKernel()
    {
        Assert.IsTrue(typeof(ILanguageKernel).IsAssignableFrom(typeof(PythonKernel)));
    }

    // ---- Integration tests (need Python runtime) ----

    [TestMethod]
    public async Task ExecuteBeforeInit_ThrowsInvalidOperationException()
    {
        var kernel = new PythonKernel();
        var context = new StubExecutionContext();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        {
            await kernel.ExecuteAsync("1 + 1", context);
        });
    }

    [TestMethod]
    public async Task ExecuteAfterDispose_ThrowsObjectDisposedException()
    {
        var kernel = new PythonKernel();
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
        var kernel = new PythonKernel();
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
        var kernel = new PythonKernel();
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
        var kernel = new PythonKernel();
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();
        await kernel.DisposeAsync(); // Should not throw
    }
}
