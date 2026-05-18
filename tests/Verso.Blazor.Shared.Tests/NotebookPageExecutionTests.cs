using Microsoft.AspNetCore.Components.Web;
using Verso.Blazor.Components.Pages;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class NotebookPageExecutionTests : BunitTestContext
{
    [TestMethod]
    public async Task RunButton_ReturnsBeforeExecutionCompletes()
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;

        var cell = new CellModel
        {
            Type = "code",
            Language = "powershell",
            Source = "Read-Host 'Name'"
        };
        var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecution = new TaskCompletionSource<ExecutionResultDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeNotebookService
        {
            IsLoaded = true,
            Cells = new List<CellModel> { cell },
            RegisteredLanguages = new List<KernelLanguageInfo>
            {
                new("powershell", "PowerShell")
            },
            ExecuteCellAsyncHandler = _ =>
            {
                executionStarted.TrySetResult();
                return releaseExecution.Task;
            }
        };
        TestContext.Services.AddSingleton<INotebookService>(service);

        var cut = RenderComponent<NotebookPage>();
        var clickTask = cut.Find("button.verso-cell-btn--run").ClickAsync(new MouseEventArgs());

        var completed = await Task.WhenAny(clickTask, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.AreSame(clickTask, completed, "Run click should return control before cell execution completes.");

        await clickTask;
        await WaitForAsync(executionStarted.Task, "Expected background execution to start.");
        Assert.IsFalse(releaseExecution.Task.IsCompleted, "Test setup should still be holding execution open.");

        releaseExecution.SetResult(new ExecutionResultDto(cell.Id, "Success", 1, TimeSpan.Zero));
    }

    [TestMethod]
    public async Task RunButton_DisabledOnOtherCellsWhileExecuting()
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;

        var cellA = new CellModel { Type = "code", Language = "powershell", Source = "1" };
        var cellB = new CellModel { Type = "code", Language = "powershell", Source = "2" };
        var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecution = new TaskCompletionSource<ExecutionResultDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeNotebookService
        {
            IsLoaded = true,
            Cells = new List<CellModel> { cellA, cellB },
            RegisteredLanguages = new List<KernelLanguageInfo>
            {
                new("powershell", "PowerShell")
            },
            ExecuteCellAsyncHandler = _ =>
            {
                executionStarted.TrySetResult();
                return releaseExecution.Task;
            }
        };
        TestContext.Services.AddSingleton<INotebookService>(service);

        var cut = RenderComponent<NotebookPage>();
        var runButtons = cut.FindAll("button.verso-cell-btn--run");
        Assert.AreEqual(2, runButtons.Count, "Expected two run buttons before any execution.");

        await runButtons[0].ClickAsync(new MouseEventArgs());
        await WaitForAsync(executionStarted.Task, "Expected background execution to start.");

        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll("button.verso-cell-btn--run");
            Assert.IsTrue(buttons[1].HasAttribute("disabled"),
                "Run button on a sibling cell should be disabled while another cell is executing.");
        });

        releaseExecution.SetResult(new ExecutionResultDto(cellA.Id, "Success", 1, TimeSpan.Zero));

        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll("button.verso-cell-btn--run");
            Assert.IsFalse(buttons[1].HasAttribute("disabled"),
                "Run button should re-enable after execution completes.");
        });
    }

    private static async Task WaitForAsync(Task task, string failureMessage)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.AreSame(task, completed, failureMessage);
        await task;
    }
}
