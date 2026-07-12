using Bunit;
using Microsoft.AspNetCore.Components;
using Verso.Abstractions;
using Verso.Blazor.Shared.Components.Notebook;
using Verso.Blazor.Shared.Tests.Fakes;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class ToolbarDiffTests : BunitTestContext
{
    private FakeNotebookService _service = default!;

    [TestInitialize]
    public void SetUpService()
    {
        _service = new FakeNotebookService();
        TestContext!.Services.AddSingleton<Verso.Blazor.Shared.Services.INotebookService>(_service);
        TestContext.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<Toolbar> RenderToolbar(
        EventCallback<NotebookDiffResult> onDiffComputed = default,
        EventCallback<string> onDiffError = default,
        EventCallback<string> onError = default)
        => TestContext!.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.Service, _service)
            .Add(p => p.OnDiffComputed, onDiffComputed)
            .Add(p => p.OnDiffError, onDiffError)
            .Add(p => p.OnError, onError));

    [TestMethod]
    public async Task ComputeDiffAsync_Success_RaisesOnDiffComputed()
    {
        _service.DiffResultToReturn = new NotebookDiffResult { BaselineLabel = "Git: HEAD" };
        NotebookDiffResult? received = null;
        var cut = RenderToolbar(
            onDiffComputed: EventCallback.Factory.Create<NotebookDiffResult>(this, r => received = r));

        await cut.InvokeAsync(() => cut.Instance.ComputeDiffAsync("gitHead", null));

        Assert.IsNotNull(received);
        Assert.AreEqual("Git: HEAD", received.BaselineLabel);
        Assert.AreEqual("gitHead", _service.LastDiffSourceId);
    }

    [TestMethod]
    public async Task ComputeDiffAsync_Cancelled_DoesNotRaiseOnDiffComputed()
    {
        _service.DiffResultToReturn = null;
        var raised = false;
        var cut = RenderToolbar(
            onDiffComputed: EventCallback.Factory.Create<NotebookDiffResult>(this, _ => raised = true));

        await cut.InvokeAsync(() => cut.Instance.ComputeDiffAsync("file", null));

        Assert.IsFalse(raised, "A cancelled picker (null result) must not open the diff view.");
    }

    [TestMethod]
    public async Task ComputeDiffAsync_Failure_RoutesToOnDiffError_NotOnError()
    {
        _service.ComputeDiffExceptionToThrow = new InvalidOperationException("'notebook.verso' is not tracked at 'HEAD'.");
        string? diffError = null;
        string? bannerError = null;
        var cut = RenderToolbar(
            onDiffError: EventCallback.Factory.Create<string>(this, m => diffError = m),
            onError: EventCallback.Factory.Create<string>(this, m => bannerError = m));

        await cut.InvokeAsync(() => cut.Instance.ComputeDiffAsync("gitHead", null));

        Assert.IsNotNull(diffError);
        StringAssert.Contains(diffError, "not tracked at 'HEAD'");
        Assert.IsNull(bannerError, "Comparison failures must use the compact dialog, not the page banner.");
    }

    [TestMethod]
    public async Task ComputeDiffAsync_Failure_NoDiffErrorHandler_FallsBackToOnError()
    {
        _service.ComputeDiffExceptionToThrow = new InvalidOperationException("boom");
        string? bannerError = null;
        var cut = RenderToolbar(
            onError: EventCallback.Factory.Create<string>(this, m => bannerError = m));

        await cut.InvokeAsync(() => cut.Instance.ComputeDiffAsync("gitHead", null));

        Assert.IsNotNull(bannerError);
        StringAssert.Contains(bannerError, "boom");
    }
}

[TestClass]
public sealed class DiffErrorDialogTests : BunitTestContext
{
    [TestMethod]
    public void NullMessage_RendersNothing()
    {
        var cut = TestContext!.RenderComponent<DiffErrorDialog>(parameters => parameters
            .Add(p => p.Message, (string?)null));

        Assert.AreEqual("", cut.Markup.Trim());
    }

    [TestMethod]
    public void Message_RendersCompactDialog_WithMessageOnly()
    {
        var cut = TestContext!.RenderComponent<DiffErrorDialog>(parameters => parameters
            .Add(p => p.Message, "'a.verso' is not tracked at 'HEAD'."));

        StringAssert.Contains(cut.Find(".verso-modal-body").TextContent, "not tracked at 'HEAD'");
        Assert.AreEqual("Compare Notebook", cut.Find(".verso-modal-title").TextContent);
    }

    [TestMethod]
    public void OkButton_InvokesOnDismiss()
    {
        var dismissed = false;
        var cut = TestContext!.RenderComponent<DiffErrorDialog>(parameters => parameters
            .Add(p => p.Message, "problem")
            .Add(p => p.OnDismiss, EventCallback.Factory.Create(this, () => dismissed = true)));

        cut.Find(".verso-modal-btn--primary").Click();

        Assert.IsTrue(dismissed);
    }
}
