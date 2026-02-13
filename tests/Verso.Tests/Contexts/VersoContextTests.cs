using Verso.Abstractions;
using Verso.Contexts;
using Verso.Stubs;

namespace Verso.Tests.Contexts;

[TestClass]
public sealed class VersoContextTests
{
    [TestMethod]
    public void Properties_Are_Accessible()
    {
        var variables = new VariableStore();
        var theme = new StubThemeContext();
        var extensionHost = new StubExtensionHostContext(() => Array.Empty<Verso.Abstractions.ILanguageKernel>());
        var metadata = new NotebookMetadataContext(new NotebookModel { Title = "Test" });
        var cts = new CancellationTokenSource();

        var context = new VersoContext(
            variables, cts.Token, theme,
            LayoutCapabilities.CellEdit, extensionHost, metadata,
            new StubNotebookOperations(),
            _ => Task.CompletedTask);

        Assert.AreSame(variables, context.Variables);
        Assert.AreEqual(cts.Token, context.CancellationToken);
        Assert.AreSame(theme, context.Theme);
        Assert.AreEqual(LayoutCapabilities.CellEdit, context.LayoutCapabilities);
        Assert.AreSame(extensionHost, context.ExtensionHost);
        Assert.AreSame(metadata, context.NotebookMetadata);
    }

    [TestMethod]
    public async Task WriteOutputAsync_Delegates_To_Callback()
    {
        CellOutput? captured = null;
        var context = CreateContext(output => { captured = output; return Task.CompletedTask; });

        var cellOutput = new CellOutput("text/plain", "hello");
        await context.WriteOutputAsync(cellOutput);

        Assert.AreSame(cellOutput, captured);
    }

    [TestMethod]
    public async Task WriteOutputAsync_NullOutput_Throws()
    {
        var context = CreateContext(_ => Task.CompletedTask);
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            () => context.WriteOutputAsync(null!));
    }

    private static VersoContext CreateContext(Func<CellOutput, Task> writeOutput)
    {
        return new VersoContext(
            new VariableStore(),
            CancellationToken.None,
            new StubThemeContext(),
            LayoutCapabilities.None,
            new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>()),
            new NotebookMetadataContext(new NotebookModel()),
            new StubNotebookOperations(),
            writeOutput);
    }
}
