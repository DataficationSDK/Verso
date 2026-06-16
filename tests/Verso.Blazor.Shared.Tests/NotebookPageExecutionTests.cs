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

    [TestMethod]
    public async Task Escape_RendersMarkdownCell_ButNotCodeCell()
    {
        // Matches Jupyter/Polyglot: leaving edit mode via Escape renders a render-only cell
        // (Markdown), while a code cell is only selected, never executed.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;

        var markdown = new CellModel { Type = "markdown", Source = "# Heading" };
        var code = new CellModel { Type = "code", Language = "powershell", Source = "1" };

        var executed = new List<Guid>();
        var markdownExecuted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeNotebookService
        {
            IsLoaded = true,
            Cells = new List<CellModel> { markdown, code },
            RegisteredLanguages = new List<KernelLanguageInfo> { new("powershell", "PowerShell") },
            CollapseInputMap = new Dictionary<string, bool> { ["markdown"] = true },
            ExecuteCellAsyncHandler = id =>
            {
                lock (executed) executed.Add(id);
                if (id == markdown.Id) markdownExecuted.TrySetResult();
                return Task.FromResult(new ExecutionResultDto(id, "Success", 1, TimeSpan.Zero));
            }
        };
        TestContext.Services.AddSingleton<INotebookService>(service);

        var cut = RenderComponent<NotebookPage>();

        // Escape out of the code cell first: a code cell is selected only, never executed.
        var codeEditor = cut.FindComponents<MonacoEditor>().First(e => e.Instance.Language == "powershell");
        await cut.InvokeAsync(() => codeEditor.Instance.OnEditorActionShortcut("escape"));

        // Escape out of the Markdown cell: a render-only cell renders (runs its renderer).
        var markdownEditor = cut.FindComponents<MonacoEditor>().First(e => e.Instance.Language == "markdown");
        await cut.InvokeAsync(() => markdownEditor.Instance.OnEditorActionShortcut("escape"));

        await WaitForAsync(markdownExecuted.Task, "Expected Escape to render the Markdown cell.");

        // By the time the Markdown render has run, the earlier code-cell Escape would have
        // executed too if it were going to, so this is a deterministic check.
        lock (executed)
        {
            CollectionAssert.Contains(executed, markdown.Id);
            CollectionAssert.DoesNotContain(executed, code.Id);
        }
    }

    [TestMethod]
    public async Task Blur_RendersMarkdownCell_ButNotCodeCell()
    {
        // Clicking away from a render-only cell's editor renders it; a code cell losing focus
        // is never executed.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;

        var markdown = new CellModel { Type = "markdown", Source = "# Heading" };
        var code = new CellModel { Type = "code", Language = "powershell", Source = "1" };

        var executed = new List<Guid>();
        var markdownExecuted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeNotebookService
        {
            IsLoaded = true,
            Cells = new List<CellModel> { markdown, code },
            RegisteredLanguages = new List<KernelLanguageInfo> { new("powershell", "PowerShell") },
            CollapseInputMap = new Dictionary<string, bool> { ["markdown"] = true },
            ExecuteCellAsyncHandler = id =>
            {
                lock (executed) executed.Add(id);
                if (id == markdown.Id) markdownExecuted.TrySetResult();
                return Task.FromResult(new ExecutionResultDto(id, "Success", 1, TimeSpan.Zero));
            }
        };
        TestContext.Services.AddSingleton<INotebookService>(service);

        var cut = RenderComponent<NotebookPage>();

        // Blur the code cell first: losing focus must not execute a code cell.
        var codeEditor = cut.FindComponents<MonacoEditor>().First(e => e.Instance.Language == "powershell");
        await cut.InvokeAsync(() => codeEditor.Instance.OnEditorActionShortcut("blur"));

        // Blur the Markdown cell: a render-only cell renders on focus loss.
        var markdownEditor = cut.FindComponents<MonacoEditor>().First(e => e.Instance.Language == "markdown");
        await cut.InvokeAsync(() => markdownEditor.Instance.OnEditorActionShortcut("blur"));

        await WaitForAsync(markdownExecuted.Task, "Expected blur to render the Markdown cell.");

        lock (executed)
        {
            CollectionAssert.Contains(executed, markdown.Id);
            CollectionAssert.DoesNotContain(executed, code.Id);
        }
    }

    [TestMethod]
    public async Task ArrowNavigation_RendersMarkdownCell_OnLeaving()
    {
        // Navigating away from a Markdown cell with the keyboard renders it, like clicking away.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;

        var markdown = new CellModel { Type = "markdown", Source = "# Heading" };
        var code = new CellModel { Type = "code", Language = "powershell", Source = "1" };

        var executed = new List<Guid>();
        var markdownExecuted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeNotebookService
        {
            IsLoaded = true,
            Cells = new List<CellModel> { markdown, code },
            RegisteredLanguages = new List<KernelLanguageInfo> { new("powershell", "PowerShell") },
            CollapseInputMap = new Dictionary<string, bool> { ["markdown"] = true },
            ExecuteCellAsyncHandler = id =>
            {
                lock (executed) executed.Add(id);
                if (id == markdown.Id) markdownExecuted.TrySetResult();
                return Task.FromResult(new ExecutionResultDto(id, "Success", 1, TimeSpan.Zero));
            }
        };
        TestContext.Services.AddSingleton<INotebookService>(service);

        var cut = RenderComponent<NotebookPage>();

        // Select the Markdown cell (index 0) by clicking its editor, then navigate down.
        cut.FindAll(".verso-cell-editor")[0].Click();
        cut.Find("div.verso-notebook").KeyDown(
            new KeyboardEventArgs { Key = "ArrowDown", CtrlKey = true, ShiftKey = true });

        await WaitForAsync(markdownExecuted.Task, "Expected navigating away to render the Markdown cell.");

        lock (executed)
        {
            CollectionAssert.Contains(executed, markdown.Id);
            CollectionAssert.DoesNotContain(executed, code.Id);
        }
    }

    [TestMethod]
    public async Task Blur_EmptyMarkdownCell_DoesNotRender()
    {
        // An empty render-only cell should not produce an empty render on focus loss.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;

        var markdown = new CellModel { Type = "markdown", Source = "   " };

        var executed = new List<Guid>();
        var service = new FakeNotebookService
        {
            IsLoaded = true,
            Cells = new List<CellModel> { markdown },
            CollapseInputMap = new Dictionary<string, bool> { ["markdown"] = true },
            ExecuteCellAsyncHandler = id =>
            {
                lock (executed) executed.Add(id);
                return Task.FromResult(new ExecutionResultDto(id, "Success", 1, TimeSpan.Zero));
            }
        };
        TestContext.Services.AddSingleton<INotebookService>(service);

        var cut = RenderComponent<NotebookPage>();

        var markdownEditor = cut.FindComponents<MonacoEditor>().First(e => e.Instance.Language == "markdown");
        await cut.InvokeAsync(() => markdownEditor.Instance.OnEditorActionShortcut("blur"));

        // Give any scheduled execution a chance to run before asserting it did not.
        await Task.Delay(100);
        lock (executed)
        {
            CollectionAssert.DoesNotContain(executed, markdown.Id);
        }
    }

    private static async Task WaitForAsync(Task task, string failureMessage)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.AreSame(task, completed, failureMessage);
        await task;
    }
}
