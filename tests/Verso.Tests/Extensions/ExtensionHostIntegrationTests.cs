using Verso.Abstractions;
using Verso.Execution;
using Verso.Extensions;
using static Verso.Execution.ExecutionResult;

namespace Verso.Tests.Extensions;

[TestClass]
public class ExtensionHostIntegrationTests
{
    // --- End-to-end: ExtensionHost discovers CSharpKernel, Scaffold executes C# cells ---

    [TestMethod]
    public async Task Scaffold_WithExtensionHost_DiscoversCSharpKernel()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        await using var scaffold = new Verso.Scaffold(new NotebookModel(), host);

        var kernel = scaffold.GetKernel("csharp");
        Assert.IsNotNull(kernel, "CSharpKernel should be discoverable through ExtensionHost.");
        Assert.AreEqual("csharp", kernel.LanguageId);
    }

    [TestMethod]
    public async Task Scaffold_WithExtensionHost_RegisteredLanguagesIncludesCSharp()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        await using var scaffold = new Verso.Scaffold(new NotebookModel(), host);

        Assert.IsTrue(scaffold.RegisteredLanguages.Contains("csharp"),
            "RegisteredLanguages should include 'csharp' from ExtensionHost.");
    }

    [TestMethod]
    public async Task Scaffold_WithExtensionHost_ExecutesCSharpCell()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        await using var scaffold = new Verso.Scaffold(new NotebookModel(), host);
        scaffold.DefaultKernelId = "csharp";

        var cell = scaffold.AddCell(language: "csharp", source: "1 + 2");
        var result = await scaffold.ExecuteCellAsync(cell.Id);

        Assert.IsTrue(result.Status == ExecutionStatus.Success, "C# cell execution should succeed.");
        Assert.IsTrue(cell.Outputs.Count > 0, "Should produce output.");
        Assert.IsTrue(cell.Outputs.Any(o => o.Content.Contains("3")),
            "Output should contain the result '3'.");
    }

    [TestMethod]
    public async Task Scaffold_WithExtensionHost_ExecutesMultipleCells()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        await using var scaffold = new Verso.Scaffold(new NotebookModel(), host);

        var cell1 = scaffold.AddCell(language: "csharp", source: "var x = 42;");
        var cell2 = scaffold.AddCell(language: "csharp", source: "x * 2");

        var result1 = await scaffold.ExecuteCellAsync(cell1.Id);
        var result2 = await scaffold.ExecuteCellAsync(cell2.Id);

        Assert.IsTrue(result1.Status == ExecutionStatus.Success);
        Assert.IsTrue(result2.Status == ExecutionStatus.Success);
        Assert.IsTrue(cell2.Outputs.Any(o => o.Content.Contains("84")),
            "Second cell should see variable from first cell.");
    }

    [TestMethod]
    public async Task Scaffold_WithExtensionHost_ExtensionHostContextReturnsHost()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        await using var scaffold = new Verso.Scaffold(new NotebookModel(), host);

        var kernels = scaffold.ExtensionHostContext.GetKernels();
        Assert.IsTrue(kernels.Any(k => k.LanguageId == "csharp"));
    }

    // --- Backward compatibility: Scaffold without ExtensionHost ---

    [TestMethod]
    public async Task Scaffold_WithoutExtensionHost_UsesStubContext()
    {
        await using var scaffold = new Verso.Scaffold();

        Assert.AreEqual(0, scaffold.RegisteredLanguages.Count);
        Assert.IsNotNull(scaffold.ExtensionHostContext);
        Assert.AreEqual(0, scaffold.ExtensionHostContext.GetKernels().Count);
    }

    [TestMethod]
    public async Task Scaffold_WithoutExtensionHost_ManualKernelStillWorks()
    {
        await using var scaffold = new Verso.Scaffold();

        var kernel = new Verso.Kernels.CSharpKernel();
        scaffold.RegisterKernel(kernel);

        var cell = scaffold.AddCell(language: "csharp", source: "2 + 2");
        var result = await scaffold.ExecuteCellAsync(cell.Id);

        Assert.IsTrue(result.Status == ExecutionStatus.Success);
    }

    // --- Manual + ExtensionHost kernels merge ---

    [TestMethod]
    public async Task Scaffold_ManualKernelOverridesExtensionHostKernel()
    {
        await using var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        await using var scaffold = new Verso.Scaffold(new NotebookModel(), host);

        var manualKernel = new Verso.Tests.Helpers.FakeLanguageKernel(languageId: "csharp");
        scaffold.RegisterKernel(manualKernel);

        var resolved = scaffold.GetKernel("csharp");
        Assert.AreSame(manualKernel, resolved,
            "Manually registered kernel should take precedence over ExtensionHost.");
    }

    // --- Dispose disposes ExtensionHost ---

    [TestMethod]
    public async Task Scaffold_Dispose_DisposesExtensionHost()
    {
        var host = new ExtensionHost();
        await host.LoadBuiltInExtensionsAsync();

        var scaffold = new Verso.Scaffold(new NotebookModel(), host);
        await scaffold.DisposeAsync();

        Assert.AreEqual(0, host.GetLoadedExtensions().Count,
            "ExtensionHost should be disposed when Scaffold is disposed.");
    }
}
