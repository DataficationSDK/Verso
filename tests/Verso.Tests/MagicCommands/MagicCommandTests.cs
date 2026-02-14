using Verso.Contexts;
using Verso.MagicCommands;
using Verso.Tests.Helpers;

namespace Verso.Tests.MagicCommands;

[TestClass]
public sealed class MagicCommandTests
{
    // --- #!time ---

    [TestMethod]
    public async Task Time_DoesNotSuppressExecution()
    {
        var command = new TimeMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.IsFalse(context.SuppressExecution);
    }

    [TestMethod]
    public async Task Time_SetsReportElapsedTime_WhenContextIsMagicCommandContext()
    {
        var command = new TimeMagicCommand();
        var stubCtx = new StubVersoContext();
        var context = new MagicCommandContext(
            "",
            stubCtx.Variables,
            CancellationToken.None,
            stubCtx.Theme,
            stubCtx.LayoutCapabilities,
            stubCtx.ExtensionHost,
            stubCtx.NotebookMetadata,
            stubCtx.Notebook,
            output => Task.CompletedTask);

        await command.ExecuteAsync("", context);

        Assert.IsTrue(context.ReportElapsedTime);
        Assert.IsFalse(context.SuppressExecution);
    }

    [TestMethod]
    public void Time_Metadata_IsCorrect()
    {
        var command = new TimeMagicCommand();

        Assert.AreEqual("time", command.Name);
        Assert.AreEqual("verso.magic.time", command.ExtensionId);
        Assert.AreEqual("0.1.0", command.Version);
    }

    // --- #!about ---

    [TestMethod]
    public async Task About_SuppressesExecution()
    {
        var command = new AboutMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.IsTrue(context.SuppressExecution);
    }

    [TestMethod]
    public async Task About_WritesVersionAndRuntimeInfo()
    {
        var command = new AboutMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.AreEqual(1, context.WrittenOutputs.Count);
        var output = context.WrittenOutputs[0];
        Assert.AreEqual("text/plain", output.MimeType);
        Assert.IsTrue(output.Content.Contains("Verso"));
        Assert.IsTrue(output.Content.Contains("Runtime:"));
        Assert.IsTrue(output.Content.Contains("OS:"));
    }

    [TestMethod]
    public void About_Metadata_IsCorrect()
    {
        var command = new AboutMagicCommand();

        Assert.AreEqual("about", command.Name);
        Assert.AreEqual("verso.magic.about", command.ExtensionId);
    }

    // --- #!restart ---

    [TestMethod]
    public async Task Restart_SuppressesExecution()
    {
        var command = new RestartMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.IsTrue(context.SuppressExecution);
    }

    [TestMethod]
    public async Task Restart_CallsRestartKernelAsync_WithNoArgument()
    {
        var command = new RestartMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var context = new StubMagicCommandContext { Notebook = notebookOps };

        await command.ExecuteAsync("", context);

        Assert.AreEqual(1, notebookOps.RestartedKernelIds.Count);
        Assert.IsNull(notebookOps.RestartedKernelIds[0]);
    }

    [TestMethod]
    public async Task Restart_CallsRestartKernelAsync_WithKernelId()
    {
        var command = new RestartMagicCommand();
        var notebookOps = new StubNotebookOperations();
        var context = new StubMagicCommandContext { Notebook = notebookOps };

        await command.ExecuteAsync("python", context);

        Assert.AreEqual(1, notebookOps.RestartedKernelIds.Count);
        Assert.AreEqual("python", notebookOps.RestartedKernelIds[0]);
    }

    [TestMethod]
    public async Task Restart_WritesConfirmationOutput()
    {
        var command = new RestartMagicCommand();
        var context = new StubMagicCommandContext();

        await command.ExecuteAsync("", context);

        Assert.AreEqual(1, context.WrittenOutputs.Count);
        Assert.IsTrue(context.WrittenOutputs[0].Content.Contains("restarted"));
    }

    [TestMethod]
    public void Restart_Metadata_IsCorrect()
    {
        var command = new RestartMagicCommand();

        Assert.AreEqual("restart", command.Name);
        Assert.AreEqual("verso.magic.restart", command.ExtensionId);
    }
}
