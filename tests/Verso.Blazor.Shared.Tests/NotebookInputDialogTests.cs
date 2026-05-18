using Verso.Blazor.Components;
using Verso.Blazor.Services;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class NotebookInputDialogTests : BunitTestContext
{
    [TestMethod]
    public void SubmitButton_InvokesSubmitWithCurrentValue()
    {
        string? submitted = null;
        var request = new ServerInputRequest(Guid.NewGuid(), "Name:", IsPassword: false);

        var cut = RenderComponent<NotebookInputDialog>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Request, request)
            .Add(p => p.OnSubmit, value => submitted = value));

        cut.Find("input").Input("Ada");
        cut.Find("button.verso-modal-btn--primary").Click();

        Assert.AreEqual("Ada", submitted);
    }

    [TestMethod]
    public void CancelButton_InvokesCancel()
    {
        var cancelled = false;
        var request = new ServerInputRequest(Guid.NewGuid(), "Name:", IsPassword: false);

        var cut = RenderComponent<NotebookInputDialog>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Request, request)
            .Add(p => p.OnCancel, () => cancelled = true));

        cut.Find("button.verso-modal-btn--secondary").Click();

        Assert.IsTrue(cancelled);
    }

    [TestMethod]
    public void PasswordRequest_RendersPasswordInput()
    {
        var request = new ServerInputRequest(Guid.NewGuid(), "Secret:", IsPassword: true);

        var cut = RenderComponent<NotebookInputDialog>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Request, request));

        Assert.AreEqual("password", cut.Find("input").GetAttribute("type"));
    }
}
