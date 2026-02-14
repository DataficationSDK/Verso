using Verso.Abstractions;
using Verso.Execution;
using Verso.Contexts;

namespace Verso.Tests.Execution;

[TestClass]
public sealed class MagicCommandPipelineTests
{
    private static (ExecutionPipeline Pipeline, TrackingKernel Kernel) CreatePipeline(
        Func<string, IMagicCommand?>? resolveMagicCommand = null)
    {
        var kernel = new TrackingKernel();
        var variables = new VariableStore();
        var theme = new Verso.Stubs.StubThemeContext();
        var extensionHost = new Verso.Stubs.StubExtensionHostContext(() => new ILanguageKernel[] { kernel });
        var notebookMetadata = new NotebookMetadataContext(new NotebookModel());
        var notebook = new Verso.Stubs.StubNotebookOperations();

        var pipeline = new ExecutionPipeline(
            variables,
            theme,
            LayoutCapabilities.CellExecute,
            extensionHost,
            notebookMetadata,
            notebook,
            languageId => string.Equals(languageId, kernel.LanguageId, StringComparison.OrdinalIgnoreCase) ? kernel : null,
            _ => Task.CompletedTask,
            _ => kernel.LanguageId,
            _ => 1,
            resolveMagicCommand);

        return (pipeline, kernel);
    }

    [TestMethod]
    public async Task MagicCommand_Suppresses_KernelNotCalled()
    {
        var magicCommand = new SuppressingMagicCommand();
        var (pipeline, kernel) = CreatePipeline(name =>
            string.Equals(name, "suppress", StringComparison.OrdinalIgnoreCase) ? magicCommand : null);

        var cell = new CellModel { Type = "code", Language = "csharp", Source = "#!suppress\nvar x = 1;" };

        var result = await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
        Assert.AreEqual(0, kernel.ExecutionCount, "Kernel should not have been called");
    }

    [TestMethod]
    public async Task MagicCommand_NoSuppress_RemainingCodeSentToKernel()
    {
        var magicCommand = new NonSuppressingMagicCommand();
        var (pipeline, kernel) = CreatePipeline(name =>
            string.Equals(name, "nosuppress", StringComparison.OrdinalIgnoreCase) ? magicCommand : null);

        var cell = new CellModel { Type = "code", Language = "csharp", Source = "#!nosuppress\nvar x = 1;" };

        var result = await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
        Assert.AreEqual(1, kernel.ExecutionCount, "Kernel should have been called");
        Assert.AreEqual("var x = 1;", kernel.LastExecutedCode, "Only remaining code should be sent");
    }

    [TestMethod]
    public async Task UnknownMagicCommand_FallsThroughToKernel()
    {
        var (pipeline, kernel) = CreatePipeline(_ => null);

        var cell = new CellModel { Type = "code", Language = "csharp", Source = "#!unknown\nvar x = 1;" };

        var result = await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
        Assert.AreEqual(1, kernel.ExecutionCount);
        Assert.AreEqual("#!unknown\nvar x = 1;", kernel.LastExecutedCode, "Full source including #! should be sent");
    }

    [TestMethod]
    public async Task NonMagicCell_Unaffected()
    {
        var (pipeline, kernel) = CreatePipeline(_ => null);

        var cell = new CellModel { Type = "code", Language = "csharp", Source = "var x = 1;" };

        var result = await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
        Assert.AreEqual(1, kernel.ExecutionCount);
        Assert.AreEqual("var x = 1;", kernel.LastExecutedCode);
    }

    [TestMethod]
    public async Task MagicCommand_NoRemainingCode_Suppresses()
    {
        var magicCommand = new NonSuppressingMagicCommand();
        var (pipeline, kernel) = CreatePipeline(name =>
            string.Equals(name, "nosuppress", StringComparison.OrdinalIgnoreCase) ? magicCommand : null);

        var cell = new CellModel { Type = "code", Language = "csharp", Source = "#!nosuppress" };

        var result = await pipeline.ExecuteAsync(cell, CancellationToken.None);

        Assert.AreEqual(ExecutionResult.ExecutionStatus.Success, result.Status);
        Assert.AreEqual(0, kernel.ExecutionCount, "Kernel should not be called when remaining code is empty");
    }

    // --- Test doubles ---

    private sealed class TrackingKernel : ILanguageKernel
    {
        public string ExtensionId => "test.tracking";
        public string Name => "Tracking Kernel";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => "Tracking kernel";
        public string LanguageId => "csharp";
        public string DisplayName => "Tracking";
        public IReadOnlyList<string> FileExtensions => Array.Empty<string>();

        public int ExecutionCount { get; private set; }
        public string? LastExecutedCode { get; private set; }

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
        public Task InitializeAsync() => Task.CompletedTask;

        public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
        {
            ExecutionCount++;
            LastExecutedCode = code;
            return Task.FromResult<IReadOnlyList<CellOutput>>(Array.Empty<CellOutput>());
        }

        public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
            => Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());
        public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
            => Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());
        public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
            => Task.FromResult<HoverInfo?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SuppressingMagicCommand : IMagicCommand
    {
        public string ExtensionId => "test.suppress";
        public string Name => "suppress";
        public string Version => "0.1.0";
        public string? Author => null;
        public string Description => "Suppresses execution.";
        public IReadOnlyList<ParameterDefinition> Parameters => Array.Empty<ParameterDefinition>();

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;

        public Task ExecuteAsync(string arguments, IMagicCommandContext context)
        {
            context.SuppressExecution = true;
            return Task.CompletedTask;
        }
    }

    private sealed class NonSuppressingMagicCommand : IMagicCommand
    {
        public string ExtensionId => "test.nosuppress";
        public string Name => "nosuppress";
        public string Version => "0.1.0";
        public string? Author => null;
        public string Description => "Does not suppress execution.";
        public IReadOnlyList<ParameterDefinition> Parameters => Array.Empty<ParameterDefinition>();

        public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;

        public Task ExecuteAsync(string arguments, IMagicCommandContext context)
        {
            context.SuppressExecution = false;
            return Task.CompletedTask;
        }
    }
}
