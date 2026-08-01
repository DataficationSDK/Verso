namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class ToolbarTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void Setup()
    {
        _service = new FakeNotebookService { IsLoaded = true, IsEmbedded = false };
    }

    // ── Visibility ─────────────────────────────────────────────────────

    [TestMethod]
    public void NewAndOpen_HiddenWhenEmbedded()
    {
        _service.IsEmbedded = true;

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Markup.Contains("New"));
        Assert.IsFalse(cut.Markup.Contains("Open"));
    }

    [TestMethod]
    public void NewAndOpen_ShownWhenNotEmbedded()
    {
        _service.IsEmbedded = false;

        var cut = RenderToolbar();

        Assert.IsTrue(cut.Markup.Contains("New"));
        Assert.IsTrue(cut.Markup.Contains("Open"));
    }

    [TestMethod]
    public void Save_ShownWhenLoaded()
    {
        _service.IsLoaded = true;

        var cut = RenderToolbar();

        Assert.IsTrue(cut.Markup.Contains("Save"));
    }

    [TestMethod]
    public void Save_HiddenWhenNotLoaded()
    {
        _service.IsLoaded = false;

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Markup.Contains("Save"));
    }

    // ── Document name ──────────────────────────────────────────────────

    [TestMethod]
    public void DocName_ShownInFull_WhenWithinLimit()
    {
        _service.FilePath = "/notebooks/report.verso";   // "report.verso" — 12 chars

        var cut = RenderToolbar();

        Assert.IsTrue(cut.Markup.Contains("report.verso"));
        Assert.IsFalse(cut.Markup.Contains("…"));
    }

    [TestMethod]
    public void DocName_TruncatedWithEllipsis_WhenTooLong()
    {
        _service.FilePath = "/notebooks/this-file-has-a-very-long-name.verso";

        var cut = RenderToolbar();

        // Displayed name is cut to 20 characters with a trailing ellipsis...
        Assert.IsTrue(cut.Markup.Contains("this-file-has-a-very…"));
        // ...while the untruncated name stays available as the element's tooltip.
        Assert.IsTrue(cut.Markup.Contains("this-file-has-a-very-long-name.verso"));
    }

    // ── Not loaded state ───────────────────────────────────────────────

    [TestMethod]
    public void WhenNotLoaded_SaveButtonHidden()
    {
        _service.IsLoaded = false;

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Markup.Contains("Save"));
    }

    // ── Action confirmation ────────────────────────────────────────────
    //
    // Actions that declare a ConfirmationPrompt (destructive ones like Restart Kernel)
    // open a confirm dialog on click and only execute once the user accepts.

    [TestMethod]
    public void ActionWithConfirmationPrompt_ClickShowsDialog_WithoutExecuting()
    {
        AddRestartAction();

        var cut = RenderToolbar();
        cut.Find("button[data-verso-tip='Restart Kernel']").Click();

        Assert.IsTrue(cut.Markup.Contains("verso-modal"));
        Assert.IsTrue(cut.Markup.Contains("Restart now?"));
        Assert.AreEqual(0, _service.ExecutedActionIds.Count);
    }

    [TestMethod]
    public void ConfirmationDialog_Confirm_ExecutesAction()
    {
        AddRestartAction();

        var cut = RenderToolbar();
        cut.Find("button[data-verso-tip='Restart Kernel']").Click();
        cut.Find(".verso-modal-footer .verso-modal-btn--primary").Click();

        CollectionAssert.Contains(_service.ExecutedActionIds, "verso.action.restart-kernel");
        Assert.IsFalse(cut.Markup.Contains("verso-modal"));
    }

    [TestMethod]
    public void ConfirmationDialog_Cancel_DoesNotExecute()
    {
        AddRestartAction();

        var cut = RenderToolbar();
        cut.Find("button[data-verso-tip='Restart Kernel']").Click();
        cut.Find(".verso-modal-footer .verso-modal-btn--secondary").Click();

        Assert.AreEqual(0, _service.ExecutedActionIds.Count);
        Assert.IsFalse(cut.Markup.Contains("verso-modal"));
    }

    [TestMethod]
    public void ActionWithoutConfirmationPrompt_ExecutesImmediately()
    {
        AddRunAllAction();

        var cut = RenderToolbar();
        cut.Find("button[data-verso-tip='Run All']").Click();

        CollectionAssert.Contains(_service.ExecutedActionIds, "verso.action.run-all");
        Assert.IsFalse(cut.Markup.Contains("verso-modal"));
    }

    // ── Run All / Stop swap ────────────────────────────────────────────

    [TestMethod]
    public void PrimaryAction_SwapsToStop_WhileExecuting()
    {
        AddRunAllAction();
        var cellId = Guid.NewGuid();

        var cut = RenderToolbar();
        cut.InvokeAsync(() => _service.RaiseCellExecuting(cellId));

        cut.WaitForAssertion(() =>
        {
            Assert.IsNotNull(cut.Find("button[data-verso-tip='Stop Execution']"));
            Assert.IsFalse(cut.Markup.Contains("Run All"));
        });
    }

    [TestMethod]
    public void StopButton_CancelsExecution()
    {
        AddRunAllAction();
        var cellId = Guid.NewGuid();

        var cut = RenderToolbar();
        cut.InvokeAsync(() => _service.RaiseCellExecuting(cellId));

        cut.WaitForElement("button[data-verso-tip='Stop Execution']").Click();

        CollectionAssert.Contains(_service.CancelledCellIds, cellId);
    }

    [TestMethod]
    public void StopButton_PersistsAcrossCellBoundaries_WhilePageExecutionInProgress()
    {
        // During a Run All batch the running-cell set momentarily drains between cells.
        // The page-level IsExecuting flag must hold the Stop button (and the Running pill)
        // through those gaps so the toolbar does not toggle at every cell boundary.
        AddRunAllAction();
        var cellId = Guid.NewGuid();

        var cut = RenderComponent<Toolbar>(p => p
            .Add(t => t.Service, _service)
            .Add(t => t.IsExecuting, true));

        cut.InvokeAsync(() => _service.RaiseCellExecuting(cellId));
        cut.InvokeAsync(() => _service.RaiseCellExecutionCompleted(cellId));

        cut.WaitForAssertion(() =>
        {
            Assert.IsNotNull(cut.Find("button[data-verso-tip='Stop Execution']"));
            Assert.IsFalse(cut.Markup.Contains("Run All"));
            Assert.IsTrue(cut.Markup.Contains("Running…"));
        });

        // The page reports the whole run finished: the primary action and idle pill return.
        cut.SetParametersAndRender(p => p.Add(t => t.IsExecuting, false));
        cut.WaitForAssertion(() =>
        {
            Assert.IsTrue(cut.Markup.Contains("Run All"));
            Assert.IsTrue(cut.Markup.Contains("Kernel idle"));
        });
    }

    [TestMethod]
    public void PrimaryAction_RestoredAfterExecutionCompletes()
    {
        AddRunAllAction();
        var cellId = Guid.NewGuid();

        var cut = RenderToolbar();
        cut.InvokeAsync(() => _service.RaiseCellExecuting(cellId));
        cut.WaitForElement("button[data-verso-tip='Stop Execution']");
        cut.InvokeAsync(() => _service.RaiseCellExecutionCompleted(cellId));

        cut.WaitForAssertion(() =>
        {
            Assert.IsTrue(cut.Markup.Contains("Run All"));
            Assert.IsFalse(cut.Markup.Contains("Stop Execution"));
        });
    }

    // ── Save dirty state ───────────────────────────────────────────────

    [TestMethod]
    public void SaveButton_ShowsDirtyDot_WhenDirty()
    {
        _service.IsDirty = true;

        var cut = RenderToolbar();

        Assert.IsTrue(cut.Markup.Contains("verso-toolbar-dirty-dot"));
        Assert.IsNotNull(cut.Find("button[data-verso-tip='Save'][data-verso-tip-desc]"));
    }

    [TestMethod]
    public void SaveButton_NoDirtyDot_WhenClean()
    {
        _service.IsDirty = false;

        var cut = RenderToolbar();

        Assert.IsFalse(cut.Markup.Contains("verso-toolbar-dirty-dot"));
    }

    // ── Kernel status pill health states ───────────────────────────────

    [TestMethod]
    public void KernelPill_ShowsDisconnected_OnHostLoss()
    {
        var cut = RenderToolbar();
        cut.InvokeAsync(() => _service.RaiseKernelHealthChanged(
            new KernelHealthChangedEventArgs(KernelHealth.Disconnected, "host process exited")));

        cut.WaitForAssertion(() =>
        {
            Assert.IsTrue(cut.Markup.Contains("Disconnected"));
            Assert.IsTrue(cut.Markup.Contains("verso-kernel-status--error"));
            // The cause is surfaced as the pill tooltip.
            Assert.IsTrue(cut.Markup.Contains("host process exited"));
        });
    }

    [TestMethod]
    public void KernelPill_ShowsError_OnFault_AndRecoversOnRestart()
    {
        var cut = RenderToolbar();
        cut.InvokeAsync(() => _service.RaiseKernelHealthChanged(
            new KernelHealthChangedEventArgs(KernelHealth.Faulted, "warm-up failed")));

        cut.WaitForAssertion(() => Assert.IsTrue(cut.Markup.Contains("Kernel error")));

        cut.InvokeAsync(() => _service.RaiseKernelRestarting());
        cut.WaitForAssertion(() => Assert.IsTrue(cut.Markup.Contains("Restarting…")));

        cut.InvokeAsync(() => _service.RaiseKernelRestarted());
        cut.WaitForAssertion(() => Assert.IsTrue(cut.Markup.Contains("Kernel idle")));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private IRenderedComponent<Toolbar> RenderToolbar()
        => RenderComponent<Toolbar>(p => p.Add(t => t.Service, _service));

    private void AddRestartAction()
    {
        _service.ToolbarActions.Add(new ToolbarActionInfo(
            "verso.action.restart-kernel", "Restart Kernel",
            "<svg viewBox=\"0 0 16 16\"></svg>",
            ToolbarPlacement.MainToolbar, 40,
            IconOnly: true,
            ConfirmationPrompt: "Restarting the kernel discards all variables and execution state. Restart now?"));
        _service.ActionEnabledStates["verso.action.restart-kernel"] = true;
    }

    private void AddRunAllAction()
    {
        _service.ToolbarActions.Add(new ToolbarActionInfo(
            "verso.action.run-all", "Run All",
            "<svg viewBox=\"0 0 16 16\"></svg>",
            ToolbarPlacement.MainToolbar, 10,
            IsPrimary: true));
        _service.ActionEnabledStates["verso.action.run-all"] = true;
    }

}

/// <summary>
/// Base class providing a bUnit <see cref="TestContext"/> scoped to each test.
/// </summary>
public abstract class BunitTestContext : TestContextWrapper
{
    [TestInitialize]
    public void BunitSetup() => TestContext = new Bunit.TestContext();

    [TestCleanup]
    public void BunitTeardown() => TestContext?.Dispose();
}
