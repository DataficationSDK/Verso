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

    private static ExecutionCtx CreateContext(
        Guid cellId, int executionCount,
        Func<CellOutput, Task> writeOutput,
        Func<CellOutput, Task> display)
    {
        return new ExecutionCtx(
            cellId, executionCount,
            new VariableStore(),
            CancellationToken.None,
            new StubThemeContext(),
            LayoutCapabilities.None,
            new StubExtensionHostContext(() => Array.Empty<Verso.Abstractions.ILanguageKernel>()),
            new NotebookMetadataContext(new NotebookModel()),
            new StubNotebookOperations(),
            writeOutput, display);
    }
}
