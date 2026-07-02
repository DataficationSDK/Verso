using Verso.Abstractions;
using Verso.Contexts;
using Verso.Execution;
using Verso.Stubs;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Execution;

[TestClass]
public sealed class ExecutionPipelineTests
{
    [TestMethod]
    public async Task HappyPath_Returns_Success()
    {
        var kernel = new FakeLanguageKernel("csharp");
        var cell = new CellModel { Language = "csharp", Source = "Console.Write(1)" };

        var pipeline = BuildPipeline(kernel, cell);
        var result = await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
        Assert.AreEqual(cell.Id, result.CellId);
        Assert.IsTrue(result.Elapsed > TimeSpan.Zero);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task Outputs_Captured_From_Return()
    {
        var kernel = new FakeLanguageKernel("csharp", executeFunc: (code, ctx) =>
            Task.FromResult<IReadOnlyList<CellOutput>>(new[] { new CellOutput("text/plain", "result") }));
        var cell = new CellModel { Language = "csharp", Source = "x" };

        var pipeline = BuildPipeline(kernel, cell);
        await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(1, cell.Outputs.Count);
        Assert.AreEqual("result", cell.Outputs[0].Content);
    }

    [TestMethod]
    public async Task StreamedOutputs_Appear_In_Cell()
    {
        var kernel = new FakeLanguageKernel("csharp", executeFunc: async (code, ctx) =>
        {
            await ctx.WriteOutputAsync(new CellOutput("text/plain", "streamed1"));
            await ctx.WriteOutputAsync(new CellOutput("text/plain", "streamed2"));
            return Array.Empty<CellOutput>();
        });
        var cell = new CellModel { Language = "csharp", Source = "x" };

        var pipeline = BuildPipeline(kernel, cell);
        await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(2, cell.Outputs.Count);
        Assert.AreEqual("streamed1", cell.Outputs[0].Content);
        Assert.AreEqual("streamed2", cell.Outputs[1].Content);
    }

    [TestMethod]
    public async Task StreamedAndReturned_Deduplicates_By_Reference()
    {
        var sharedOutput = new CellOutput("text/plain", "shared");
        var kernel = new FakeLanguageKernel("csharp", executeFunc: async (code, ctx) =>
        {
            await ctx.WriteOutputAsync(sharedOutput);
            return new[] { sharedOutput, new CellOutput("text/plain", "extra") };
        });
        var cell = new CellModel { Language = "csharp", Source = "x" };

        var pipeline = BuildPipeline(kernel, cell);
        await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(2, cell.Outputs.Count);
        Assert.AreEqual("shared", cell.Outputs[0].Content);
        Assert.AreEqual("extra", cell.Outputs[1].Content);
    }

    [TestMethod]
    public async Task Elapsed_Time_Is_Measured()
    {
        var kernel = new FakeLanguageKernel("csharp", executeFunc: async (code, ctx) =>
        {
            await Task.Delay(50);
            return Array.Empty<CellOutput>();
        });
        var cell = new CellModel { Language = "csharp", Source = "x" };

        var pipeline = BuildPipeline(kernel, cell);
        var result = await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.IsTrue(result.Elapsed >= TimeSpan.FromMilliseconds(30));
    }

    [TestMethod]
    public async Task NoLanguage_Returns_Failed()
    {
        var cell = new CellModel { Language = null, Source = "x" };

        var pipeline = BuildPipeline(null, cell, defaultKernelId: null);
        var result = await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Failed, result.Status);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public async Task UnregisteredKernel_Returns_Failed()
    {
        var cell = new CellModel { Language = "unknown", Source = "x" };

        var pipeline = BuildPipeline(null, cell);
        var result = await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Failed, result.Status);
    }

    [TestMethod]
    public async Task RenderOnlyCell_FiresOutputNotification()
    {
        // Regression: render-only cells (e.g. Markdown) must raise the output-updated
        // notification just like kernel execution does, so out-of-process hosts receive
        // their content incrementally as the cell finishes instead of only at the end of a
        // Run All. Previously the render path added the output without notifying, so these
        // cells appeared blank until the whole batch completed.
        var renderer = new FakeCellRenderer(cellTypeId: "markdown");
        var cell = new CellModel { Type = "markdown", Source = "# Title" };

        var notified = new List<Guid>();
        var pipeline = BuildPipeline(
            kernel: null, cell,
            renderers: new ICellRenderer[] { renderer },
            notifyOutputUpdated: notified.Add);

        var result = await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
        Assert.AreEqual(1, cell.Outputs.Count);
        Assert.AreEqual("input:# Title", cell.Outputs[0].Content);
        CollectionAssert.Contains(notified, cell.Id, "Render-only cell must notify that its output updated.");
    }

    private static ExecutionPipeline BuildPipeline(
        FakeLanguageKernel? kernel, CellModel cell, string? defaultKernelId = null,
        IReadOnlyList<ICellRenderer>? renderers = null,
        Action<Guid>? notifyOutputUpdated = null)
    {
        var variables = new VariableStore();
        var theme = new StubThemeContext();
        var extensionHost = new StubExtensionHostContext(
            () => Array.Empty<ILanguageKernel>(),
            renderers is null ? null : () => renderers);
        var metadata = new NotebookMetadataContext(new NotebookModel { DefaultKernelId = defaultKernelId });

        return new ExecutionPipeline(
            variables, theme, LayoutCapabilities.None, extensionHost, metadata,
            new Verso.Stubs.StubNotebookOperations(),
            resolveKernel: langId => kernel?.LanguageId.Equals(langId, StringComparison.OrdinalIgnoreCase) == true ? kernel : null,
            ensureInitialized: k => k.InitializeAsync(),
            resolveLanguageId: _ => cell.Language ?? defaultKernelId,
            getExecutionCount: _ => 1,
            notifyOutputUpdated: notifyOutputUpdated);
    }
}
