using Microsoft.AspNetCore.Components.Web;
using Verso.Blazor.Components.Pages;

namespace Verso.Blazor.Shared.Tests;

/// <summary>
/// Command-mode keys on the notebook container: they act when the notebook itself holds
/// the keyboard and stay quiet while a text field does.
/// </summary>
[TestClass]
public sealed class NotebookPageKeyboardTests : BunitTestContext
{
    private const string FocusProbe = "versoKeyboard.isEditableElementFocused";

    [TestMethod]
    public async Task InsertKey_InsertsCellAbove_WhenNoTextFieldHasFocus()
    {
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        TestContext.JSInterop.Setup<bool>(FocusProbe).SetResult(false);
        var service = RegisterService(out _);

        var cut = RenderComponent<NotebookPage>();
        cut.Find(".verso-cell-editor").Click();
        await cut.Find("div.verso-notebook").KeyDownAsync(new KeyboardEventArgs { Key = "a" });

        Assert.AreEqual(1, service.InsertCellCalls.Count, "Expected \"a\" in command mode to insert a cell.");
        Assert.AreEqual((0, "code"), service.InsertCellCalls[0]);
    }

    [TestMethod]
    public async Task CommandKeys_AreIgnored_WhileTextFieldHasFocus()
    {
        // A parameters cell's name field is a plain input inside the notebook, so its
        // keystrokes bubble up to the container like any other. Typing there must not
        // insert or delete cells.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        TestContext.JSInterop.Setup<bool>(FocusProbe).SetResult(true);
        var service = RegisterService(out _);

        var cut = RenderComponent<NotebookPage>();
        cut.Find(".verso-cell-editor").Click();
        var container = cut.Find("div.verso-notebook");
        foreach (var key in new[] { "a", "b", "x", "d", "d" })
            await container.KeyDownAsync(new KeyboardEventArgs { Key = key });

        Assert.AreEqual(0, service.InsertCellCalls.Count, "A key typed into a text field inserted a cell.");
        Assert.AreEqual(0, service.RemoveCellCalls.Count, "A key typed into a text field deleted a cell.");
    }

    [TestMethod]
    public async Task PendingDeleteSequence_IsCancelled_ByTypingInTextField()
    {
        // The first "d" arms a delete. Typing in a field afterwards must disarm it, so a
        // later lone "d" back in command mode does not delete the cell.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        var probe = TestContext.JSInterop.Setup<bool>(FocusProbe);
        var service = RegisterService(out _);

        var cut = RenderComponent<NotebookPage>();
        cut.Find(".verso-cell-editor").Click();
        var container = cut.Find("div.verso-notebook");

        probe.SetResult(false);
        await container.KeyDownAsync(new KeyboardEventArgs { Key = "d" });
        probe.SetResult(true);
        await container.KeyDownAsync(new KeyboardEventArgs { Key = "a" });
        probe.SetResult(false);
        await container.KeyDownAsync(new KeyboardEventArgs { Key = "d" });

        Assert.AreEqual(0, service.RemoveCellCalls.Count, "A delete armed before typing in a field survived it.");
    }

    [TestMethod]
    public async Task RunAll_StillWorks_WhileTextFieldHasFocus()
    {
        // Run All is the one shortcut that works in every mode, and a text field is no exception.
        TestContext!.JSInterop.Mode = JSRuntimeMode.Loose;
        TestContext.JSInterop.Setup<bool>(FocusProbe).SetResult(true);
        var ranAll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = RegisterService(out _);
        service.ExecuteAllAsyncHandler = () =>
        {
            ranAll.TrySetResult();
            return Task.FromResult<IReadOnlyList<ExecutionResultDto>>(Array.Empty<ExecutionResultDto>());
        };

        var cut = RenderComponent<NotebookPage>();
        cut.Find(".verso-cell-editor").Click();
        await cut.Find("div.verso-notebook").KeyDownAsync(
            new KeyboardEventArgs { Key = "Enter", CtrlKey = true, AltKey = true });

        // Run All is handed to a background task, so wait for it rather than checking a flag.
        var completed = await Task.WhenAny(ranAll.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.AreSame(ranAll.Task, completed, "Expected Ctrl+Alt+Enter to run all cells from inside a text field.");
    }

    private FakeNotebookService RegisterService(out CellModel cell)
    {
        cell = new CellModel { Type = "code", Language = "powershell", Source = "1" };
        var service = new FakeNotebookService
        {
            IsLoaded = true,
            Cells = new List<CellModel> { cell },
            RegisteredLanguages = new List<KernelLanguageInfo> { new("powershell", "PowerShell") }
        };
        TestContext!.Services.AddSingleton<INotebookService>(service);
        return service;
    }
}
