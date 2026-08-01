using Bunit;
using Microsoft.AspNetCore.Components;
using Verso.Blazor.Shared.Components.Notebook;

namespace Verso.Blazor.Shared.Tests;

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
