using Verso.Abstractions;
using Verso.JavaScript.Kernel;
using Verso.Testing.Stubs;

namespace Verso.JavaScript.Tests.Kernel;

[TestClass]
public class LifecycleTests
{
    [TestMethod]
    public void Metadata_HasCorrectValues()
    {
        var kernel = new JavaScriptKernel();
        Assert.AreEqual("verso.kernel.javascript", kernel.ExtensionId);
        Assert.AreEqual("JavaScript", kernel.Name);
        Assert.AreEqual("1.0.0", kernel.Version);
        Assert.AreEqual("Verso Contributors", kernel.Author);
        Assert.IsNotNull(kernel.Description);
    }

    [TestMethod]
    public void LanguageProperties_AreCorrect()
    {
        var kernel = new JavaScriptKernel();
        Assert.AreEqual("javascript", kernel.LanguageId);
        Assert.AreEqual("JavaScript", kernel.DisplayName);
        Assert.IsTrue(kernel.FileExtensions.Contains(".js"));
        Assert.IsTrue(kernel.FileExtensions.Contains(".mjs"));
    }

    [TestMethod]
    public async Task ExecuteAfterDispose_ThrowsObjectDisposedException()
    {
        var kernel = new JavaScriptKernel(new JavaScriptKernelOptions { ForceJint = true });
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
        var kernel = new JavaScriptKernel(new JavaScriptKernelOptions { ForceJint = true });
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();

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
        var kernel = new JavaScriptKernel(new JavaScriptKernelOptions { ForceJint = true });
        await kernel.InitializeAsync();
        await kernel.InitializeAsync();

        var context = new StubExecutionContext();
        var outputs = await kernel.ExecuteAsync("42", context);
        Assert.IsTrue(outputs.Count > 0);

        await kernel.DisposeAsync();
    }

    [TestMethod]
    public async Task DoubleDispose_IsIdempotent()
    {
        var kernel = new JavaScriptKernel(new JavaScriptKernelOptions { ForceJint = true });
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();
        await kernel.DisposeAsync();
    }

    [TestMethod]
    public void HasVersoExtensionAttribute()
    {
        var attr = Attribute.GetCustomAttribute(
            typeof(JavaScriptKernel), typeof(VersoExtensionAttribute));
        Assert.IsNotNull(attr, "JavaScriptKernel should have [VersoExtension] attribute");
    }

    [TestMethod]
    public void ImplementsILanguageKernel()
    {
        Assert.IsTrue(typeof(ILanguageKernel).IsAssignableFrom(typeof(JavaScriptKernel)));
    }

    [TestMethod]
    public async Task GetCompletions_ReturnsEmpty()
    {
        var kernel = new JavaScriptKernel();
        var completions = await kernel.GetCompletionsAsync("console", 7);
        Assert.IsNotNull(completions);
        Assert.AreEqual(0, completions.Count);
    }

    [TestMethod]
    public async Task GetDiagnostics_ReturnsEmpty()
    {
        var kernel = new JavaScriptKernel();
        var diagnostics = await kernel.GetDiagnosticsAsync("let x = 1;");
        Assert.IsNotNull(diagnostics);
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task GetHoverInfo_ReturnsNull()
    {
        var kernel = new JavaScriptKernel();
        var hover = await kernel.GetHoverInfoAsync("let x = 1;", 5);
        Assert.IsNull(hover);
    }
}
