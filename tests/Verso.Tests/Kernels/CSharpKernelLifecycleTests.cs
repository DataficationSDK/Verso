using Verso.Abstractions;
using Verso.Kernels;
using Verso.Stubs;

namespace Verso.Tests.Kernels;

[TestClass]
public sealed class CSharpKernelLifecycleTests
{
    [TestMethod]
    public void Metadata_ExtensionId_IsCorrect()
    {
        var kernel = new CSharpKernel();
        Assert.AreEqual("verso.kernel.csharp", kernel.ExtensionId);
    }

    [TestMethod]
    public void Metadata_LanguageId_IsCSharp()
    {
        var kernel = new CSharpKernel();
        Assert.AreEqual("csharp", kernel.LanguageId);
    }

    [TestMethod]
    public void Metadata_DisplayName_IsSet()
    {
        var kernel = new CSharpKernel();
        Assert.AreEqual("C# (Roslyn)", kernel.DisplayName);
    }

    [TestMethod]
    public void Metadata_FileExtensions_IncludeCsAndCsx()
    {
        var kernel = new CSharpKernel();
        CollectionAssert.Contains(kernel.FileExtensions.ToList(), ".cs");
        CollectionAssert.Contains(kernel.FileExtensions.ToList(), ".csx");
    }

    [TestMethod]
    public void Metadata_Version_IsSet()
    {
        var kernel = new CSharpKernel();
        Assert.AreEqual("0.1.0", kernel.Version);
    }

    [TestMethod]
    public void Metadata_Name_IsSet()
    {
        var kernel = new CSharpKernel();
        Assert.AreEqual("C# (Roslyn)", kernel.Name);
    }

    [TestMethod]
    public void Metadata_Author_IsSet()
    {
        var kernel = new CSharpKernel();
        Assert.IsNotNull(kernel.Author);
    }

    [TestMethod]
    public void Metadata_Description_IsSet()
    {
        var kernel = new CSharpKernel();
        Assert.IsNotNull(kernel.Description);
    }

    [TestMethod]
    public async Task Initialize_IsIdempotent()
    {
        await using var kernel = new CSharpKernel();
        await kernel.InitializeAsync();
        await kernel.InitializeAsync(); // Second call should be no-op
    }

    [TestMethod]
    public async Task Dispose_IsIdempotent()
    {
        var kernel = new CSharpKernel();
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();
        await kernel.DisposeAsync(); // Second call should be no-op
    }

    [TestMethod]
    public async Task Execute_AfterDispose_Throws()
    {
        var kernel = new CSharpKernel();
        await kernel.InitializeAsync();
        await kernel.DisposeAsync();

        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(
            () => kernel.ExecuteAsync("1 + 1", CreateContext()));
    }

    [TestMethod]
    public async Task Execute_BeforeInit_Throws()
    {
        var kernel = new CSharpKernel();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => kernel.ExecuteAsync("1 + 1", CreateContext()));
    }

    [TestMethod]
    public async Task OnLoadedAsync_DoesNotThrow()
    {
        var kernel = new CSharpKernel();
        var extensionHost = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
        await kernel.OnLoadedAsync(extensionHost);
    }

    [TestMethod]
    public async Task OnUnloadedAsync_DoesNotThrow()
    {
        var kernel = new CSharpKernel();
        await kernel.OnUnloadedAsync();
    }

    [TestMethod]
    public void HasVersoExtensionAttribute()
    {
        var attr = Attribute.GetCustomAttribute(typeof(CSharpKernel), typeof(VersoExtensionAttribute));
        Assert.IsNotNull(attr, "CSharpKernel should have the [VersoExtension] attribute.");
    }

    [TestMethod]
    public void ImplementsILanguageKernel()
    {
        Assert.IsTrue(typeof(ILanguageKernel).IsAssignableFrom(typeof(CSharpKernel)));
    }

    [TestMethod]
    public void ImplementsIAsyncDisposable()
    {
        Assert.IsTrue(typeof(IAsyncDisposable).IsAssignableFrom(typeof(CSharpKernel)));
    }

    [TestMethod]
    public void ImplementsIExtension()
    {
        Assert.IsTrue(typeof(IExtension).IsAssignableFrom(typeof(CSharpKernel)));
    }

    [TestMethod]
    public void Constructor_NullOptions_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new CSharpKernel(null!));
    }

    [TestMethod]
    public void DefaultConstructor_DoesNotThrow()
    {
        var kernel = new CSharpKernel();
        Assert.IsNotNull(kernel);
    }

    private static Verso.Contexts.ExecutionContext CreateContext()
    {
        var variables = new Verso.Contexts.VariableStore();
        var theme = new StubThemeContext();
        var extensionHost = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
        var metadata = new Verso.Contexts.NotebookMetadataContext(new NotebookModel());

        return new Verso.Contexts.ExecutionContext(
            Guid.NewGuid(), 1, variables, CancellationToken.None,
            theme, LayoutCapabilities.None, extensionHost, metadata,
            new Verso.Stubs.StubNotebookOperations(),
            writeOutput: _ => Task.CompletedTask,
            display: _ => Task.CompletedTask);
    }
}
