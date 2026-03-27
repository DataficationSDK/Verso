using Verso.Abstractions;
using Verso.JavaScript.Kernel;

namespace Verso.JavaScript.Tests.Kernel;

[TestClass]
public class TypeScriptKernelTests
{
    [TestMethod]
    public void Metadata_HasCorrectValues()
    {
        var kernel = new TypeScriptKernel();
        Assert.AreEqual("verso.kernel.typescript", kernel.ExtensionId);
        Assert.AreEqual("TypeScript", kernel.Name);
        Assert.AreEqual("1.0.0", kernel.Version);
        Assert.AreEqual("Verso Contributors", kernel.Author);
        Assert.IsNotNull(kernel.Description);
    }

    [TestMethod]
    public void LanguageProperties_AreCorrect()
    {
        var kernel = new TypeScriptKernel();
        Assert.AreEqual("typescript", kernel.LanguageId);
        Assert.AreEqual("TypeScript", kernel.DisplayName);
        Assert.IsTrue(kernel.FileExtensions.Contains(".ts"));
        Assert.IsTrue(kernel.FileExtensions.Contains(".tsx"));
    }

    [TestMethod]
    public void HasVersoExtensionAttribute()
    {
        var attr = Attribute.GetCustomAttribute(
            typeof(TypeScriptKernel), typeof(VersoExtensionAttribute));
        Assert.IsNotNull(attr, "TypeScriptKernel should have [VersoExtension] attribute");
    }

    [TestMethod]
    public void ImplementsILanguageKernel()
    {
        Assert.IsTrue(typeof(ILanguageKernel).IsAssignableFrom(typeof(TypeScriptKernel)));
    }

    [TestMethod]
    public async Task GetCompletions_ReturnsEmpty()
    {
        var kernel = new TypeScriptKernel();
        var completions = await kernel.GetCompletionsAsync("const x: number = 1", 20);
        Assert.IsNotNull(completions);
        Assert.AreEqual(0, completions.Count);
    }

    [TestMethod]
    public async Task GetDiagnostics_ReturnsEmpty()
    {
        var kernel = new TypeScriptKernel();
        var diagnostics = await kernel.GetDiagnosticsAsync("const x: number = 1;");
        Assert.IsNotNull(diagnostics);
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task GetHoverInfo_ReturnsNull()
    {
        var kernel = new TypeScriptKernel();
        var hover = await kernel.GetHoverInfoAsync("const x: number = 1;", 5);
        Assert.IsNull(hover);
    }

    [TestMethod]
    public async Task ExecuteAfterDispose_ThrowsObjectDisposedException()
    {
        var kernel = new TypeScriptKernel(new JavaScriptKernelOptions());
        // TypeScript kernel requires Node.js to initialize, so we test the dispose guard directly
        // by calling DisposeAsync on a never-initialized kernel and then trying to execute
        await kernel.DisposeAsync();

        var context = new Testing.Stubs.StubExecutionContext();
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(async () =>
        {
            await kernel.ExecuteAsync("const x: number = 1", context);
        });
    }

    [TestMethod]
    public async Task DoubleDispose_IsIdempotent()
    {
        var kernel = new TypeScriptKernel(new JavaScriptKernelOptions());
        await kernel.DisposeAsync();
        await kernel.DisposeAsync(); // Should not throw
    }
}
