using Verso.Abstractions;
using Verso.Extensions.ToolbarActions;
using Verso.Tests.Helpers;

namespace Verso.Tests.Extensions.ToolbarActions;

[TestClass]
public sealed class ToolbarActionTests
{
    // --- RunAllAction ---

    [TestMethod]
    public async Task RunAll_IsEnabled_WhenCellExecuteAndHasCells()
    {
        var action = new RunAllAction();
        var context = CreateContext(
            capabilities: LayoutCapabilities.CellExecute,
            cells: new[] { new CellModel() });

        Assert.IsTrue(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task RunAll_IsDisabled_WhenNoCellExecuteFlag()
    {
        var action = new RunAllAction();
        var context = CreateContext(capabilities: LayoutCapabilities.None,
            cells: new[] { new CellModel() });

        Assert.IsFalse(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task RunAll_IsDisabled_WhenNoCells()
    {
        var action = new RunAllAction();
        var context = CreateContext(capabilities: LayoutCapabilities.CellExecute,
            cells: Array.Empty<CellModel>());

        Assert.IsFalse(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task RunAll_Execute_CallsExecuteAll()
    {
        var action = new RunAllAction();
        var stub = new Helpers.StubNotebookOperations();
        var context = CreateContext(notebook: stub);

        await action.ExecuteAsync(context);

        Assert.AreEqual(1, stub.ExecuteAllCallCount);
    }

    [TestMethod]
    public void RunAll_Metadata_IsCorrect()
    {
        var action = new RunAllAction();
        Assert.AreEqual("verso.action.run-all", action.ActionId);
        Assert.AreEqual(ToolbarPlacement.MainToolbar, action.Placement);
        Assert.AreEqual(10, action.Order);
    }

    // --- RunCellAction ---

    [TestMethod]
    public async Task RunCell_IsEnabled_WhenCellExecuteAndHasSelection()
    {
        var action = new RunCellAction();
        var context = CreateContext(
            capabilities: LayoutCapabilities.CellExecute,
            selectedCellIds: new[] { Guid.NewGuid() });

        Assert.IsTrue(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task RunCell_IsDisabled_WhenNoCellExecuteFlag()
    {
        var action = new RunCellAction();
        var context = CreateContext(
            capabilities: LayoutCapabilities.None,
            selectedCellIds: new[] { Guid.NewGuid() });

        Assert.IsFalse(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task RunCell_IsDisabled_WhenNoSelection()
    {
        var action = new RunCellAction();
        var context = CreateContext(
            capabilities: LayoutCapabilities.CellExecute,
            selectedCellIds: Array.Empty<Guid>());

        Assert.IsFalse(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task RunCell_Execute_ExecutesSelectedCells()
    {
        var action = new RunCellAction();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var stub = new Helpers.StubNotebookOperations();
        var context = CreateContext(
            selectedCellIds: new[] { id1, id2 },
            notebook: stub);

        await action.ExecuteAsync(context);

        Assert.AreEqual(2, stub.ExecutedCellIds.Count);
        Assert.AreEqual(id1, stub.ExecutedCellIds[0]);
        Assert.AreEqual(id2, stub.ExecutedCellIds[1]);
    }

    [TestMethod]
    public void RunCell_Metadata_IsCorrect()
    {
        var action = new RunCellAction();
        Assert.AreEqual("verso.action.run-cell", action.ActionId);
        Assert.AreEqual(ToolbarPlacement.CellToolbar, action.Placement);
        Assert.AreEqual(20, action.Order);
    }

    // --- ClearOutputsAction ---

    [TestMethod]
    public async Task ClearOutputs_IsEnabled_WhenHasCells()
    {
        var action = new ClearOutputsAction();
        var context = CreateContext(cells: new[] { new CellModel() });

        Assert.IsTrue(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task ClearOutputs_IsDisabled_WhenNoCells()
    {
        var action = new ClearOutputsAction();
        var context = CreateContext(cells: Array.Empty<CellModel>());

        Assert.IsFalse(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task ClearOutputs_Execute_CallsClearAllOutputs()
    {
        var action = new ClearOutputsAction();
        var stub = new Helpers.StubNotebookOperations();
        var context = CreateContext(notebook: stub);

        await action.ExecuteAsync(context);

        Assert.AreEqual(1, stub.ClearAllOutputsCallCount);
    }

    [TestMethod]
    public void ClearOutputs_Metadata_IsCorrect()
    {
        var action = new ClearOutputsAction();
        Assert.AreEqual("verso.action.clear-outputs", action.ActionId);
        Assert.AreEqual(ToolbarPlacement.MainToolbar, action.Placement);
        Assert.AreEqual(30, action.Order);
    }

    // --- RestartKernelAction ---

    [TestMethod]
    public async Task RestartKernel_IsEnabled_WhenActiveKernelSet()
    {
        var action = new RestartKernelAction();
        var context = CreateContext(activeKernelId: "csharp");

        Assert.IsTrue(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task RestartKernel_IsDisabled_WhenNoActiveKernel()
    {
        var action = new RestartKernelAction();
        var context = CreateContext(activeKernelId: null);

        Assert.IsFalse(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task RestartKernel_Execute_CallsRestartWithActiveKernelId()
    {
        var action = new RestartKernelAction();
        var stub = new Helpers.StubNotebookOperations();
        var context = CreateContext(activeKernelId: "csharp", notebook: stub);

        await action.ExecuteAsync(context);

        Assert.AreEqual(1, stub.RestartedKernelIds.Count);
        Assert.AreEqual("csharp", stub.RestartedKernelIds[0]);
    }

    [TestMethod]
    public void RestartKernel_Metadata_IsCorrect()
    {
        var action = new RestartKernelAction();
        Assert.AreEqual("verso.action.restart-kernel", action.ActionId);
        Assert.AreEqual(ToolbarPlacement.MainToolbar, action.Placement);
        Assert.AreEqual(40, action.Order);
    }

    // --- ExportHtmlAction ---

    [TestMethod]
    public async Task ExportHtml_IsEnabled_WhenHasCells()
    {
        var action = new ExportHtmlAction();
        var context = CreateContext(cells: new[] { new CellModel() });

        Assert.IsTrue(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task ExportHtml_IsDisabled_WhenNoCells()
    {
        var action = new ExportHtmlAction();
        var context = CreateContext(cells: Array.Empty<CellModel>());

        Assert.IsFalse(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public void ExportHtml_Metadata_IsCorrect()
    {
        var action = new ExportHtmlAction();
        Assert.AreEqual("verso.action.export-html", action.ActionId);
        Assert.AreEqual(ToolbarPlacement.MainToolbar, action.Placement);
        Assert.AreEqual(60, action.Order);
    }

    [TestMethod]
    public async Task ExportHtml_Execute_CallsRequestFileDownload()
    {
        var action = new ExportHtmlAction();
        var context = CreateContext(cells: new[] { new CellModel { Type = "code", Source = "x" } });

        await action.ExecuteAsync(context);

        Assert.AreEqual(1, context.DownloadedFiles.Count);
        Assert.IsTrue(context.DownloadedFiles[0].FileName.EndsWith(".html"));
        Assert.AreEqual("text/html", context.DownloadedFiles[0].ContentType);
        Assert.IsTrue(context.DownloadedFiles[0].Data.Length > 0);
    }

    // --- ExportMarkdownAction ---

    [TestMethod]
    public async Task ExportMarkdown_IsEnabled_WhenHasCells()
    {
        var action = new ExportMarkdownAction();
        var context = CreateContext(cells: new[] { new CellModel() });

        Assert.IsTrue(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public async Task ExportMarkdown_IsDisabled_WhenNoCells()
    {
        var action = new ExportMarkdownAction();
        var context = CreateContext(cells: Array.Empty<CellModel>());

        Assert.IsFalse(await action.IsEnabledAsync(context));
    }

    [TestMethod]
    public void ExportMarkdown_Metadata_IsCorrect()
    {
        var action = new ExportMarkdownAction();
        Assert.AreEqual("verso.action.export-markdown", action.ActionId);
        Assert.AreEqual(ToolbarPlacement.MainToolbar, action.Placement);
        Assert.AreEqual(65, action.Order);
    }

    [TestMethod]
    public async Task ExportMarkdown_Execute_CallsRequestFileDownload()
    {
        var action = new ExportMarkdownAction();
        var context = CreateContext(cells: new[] { new CellModel { Type = "code", Source = "x" } });

        await action.ExecuteAsync(context);

        Assert.AreEqual(1, context.DownloadedFiles.Count);
        Assert.IsTrue(context.DownloadedFiles[0].FileName.EndsWith(".md"));
        Assert.AreEqual("text/markdown", context.DownloadedFiles[0].ContentType);
        Assert.IsTrue(context.DownloadedFiles[0].Data.Length > 0);
    }

    // --- Helper ---

    private static StubToolbarActionContext CreateContext(
        LayoutCapabilities capabilities = LayoutCapabilities.CellExecute,
        CellModel[]? cells = null,
        Guid[]? selectedCellIds = null,
        string? activeKernelId = "csharp",
        Helpers.StubNotebookOperations? notebook = null)
    {
        return new StubToolbarActionContext
        {
            LayoutCapabilities = capabilities,
            NotebookCells = cells ?? new[] { new CellModel() },
            SelectedCellIds = selectedCellIds ?? Array.Empty<Guid>(),
            ActiveKernelId = activeKernelId,
            Notebook = notebook ?? new Helpers.StubNotebookOperations()
        };
    }
}
