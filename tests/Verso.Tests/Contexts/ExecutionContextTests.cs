using Verso.Contexts;
using Verso.Stubs;
using ExecutionCtx = Verso.Contexts.ExecutionContext;

namespace Verso.Tests.Contexts;

[TestClass]
public sealed class ExecutionContextTests
{
    [TestMethod]
    public void Properties_Are_Accessible()
    {
        var cellId = Guid.NewGuid();
        var context = CreateContext(cellId, 5, _ => Task.CompletedTask, _ => Task.CompletedTask);

        Assert.AreEqual(cellId, context.CellId);
        Assert.AreEqual(5, context.ExecutionCount);
    }

    [TestMethod]
    public async Task WriteOutputAsync_Invokes_Delegate()
    {
        CellOutput? captured = null;
        var context = CreateContext(Guid.NewGuid(), 1,
            output => { captured = output; return Task.CompletedTask; },
            _ => Task.CompletedTask);

        var cellOutput = new CellOutput("text/plain", "test");
        await context.WriteOutputAsync(cellOutput);

        Assert.AreSame(cellOutput, captured);
    }

    [TestMethod]
    public async Task DisplayAsync_Invokes_Delegate()
    {
        CellOutput? captured = null;
        var context = CreateContext(Guid.NewGuid(), 1,
            _ => Task.CompletedTask,
            output => { captured = output; return Task.CompletedTask; });

        var cellOutput = new CellOutput("text/html", "<b>rich</b>");
        await context.DisplayAsync(cellOutput);

        Assert.AreSame(cellOutput, captured);
    }

    [TestMethod]
    public async Task DisplayAsync_NullOutput_Throws()
    {
        var context = CreateContext(Guid.NewGuid(), 1, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            () => context.DisplayAsync(null!));
    }

    [TestMethod]
    public void Implements_IExecutionContext()
    {
        var context = CreateContext(Guid.NewGuid(), 1, _ => Task.CompletedTask, _ => Task.CompletedTask);
        Assert.IsInstanceOfType(context, typeof(Verso.Abstractions.IExecutionContext));
    }

    [TestMethod]
    public async Task TryFormatAsync_UsesSpecializedFormatter()
    {
        var formatter = new TestFormatter(isFallback: false);
        var host = new StubExtensionHostContext(
            () => Array.Empty<Verso.Abstractions.ILanguageKernel>(),
            getFormatters: () => new[] { formatter });
        var context = CreateContext(
            Guid.NewGuid(), 1,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            host);

        var output = await context.TryFormatAsync("value");

        Assert.IsNotNull(output);
        Assert.AreEqual("specialized", output.Content);
    }

    [TestMethod]
    public async Task TryFormatAsync_SkipsFallbackFormatter()
    {
        var formatter = new TestFormatter(isFallback: true);
        var host = new StubExtensionHostContext(
            () => Array.Empty<Verso.Abstractions.ILanguageKernel>(),
            getFormatters: () => new[] { formatter });
        var context = CreateContext(
            Guid.NewGuid(), 1,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            host);

        var output = await context.TryFormatAsync("value");

        Assert.IsNull(output);
    }

    private static ExecutionCtx CreateContext(
        Guid cellId, int executionCount,
        Func<CellOutput, Task> writeOutput,
        Func<CellOutput, Task> display,
        Verso.Abstractions.IExtensionHostContext? extensionHost = null)
    {
        return new ExecutionCtx(
            cellId, executionCount,
            new VariableStore(),
            CancellationToken.None,
            new StubThemeContext(),
            LayoutCapabilities.None,
            extensionHost ?? new StubExtensionHostContext(() => Array.Empty<Verso.Abstractions.ILanguageKernel>()),
            new NotebookMetadataContext(new NotebookModel()),
            new StubNotebookOperations(),
            writeOutput, display);
    }

    private sealed class TestFormatter : Verso.Abstractions.IDataFormatter
    {
        public TestFormatter(bool isFallback) => IsFallback = isFallback;

        public string ExtensionId => "com.test.execution-context.formatter";
        public string Name => "Execution Context Formatter";
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public IReadOnlyList<Type> SupportedTypes { get; } = new[] { typeof(string) };
        public int Priority => 10;
        public bool IsFallback { get; }
        public Task OnLoadedAsync(Verso.Abstractions.IExtensionHostContext context) => Task.CompletedTask;
        public Task OnUnloadedAsync() => Task.CompletedTask;
        public bool CanFormat(object value, Verso.Abstractions.IFormatterContext context) => value is string;
        public Task<CellOutput> FormatAsync(object value, Verso.Abstractions.IFormatterContext context)
            => Task.FromResult(CellOutput.Plain("specialized"));
    }
}
